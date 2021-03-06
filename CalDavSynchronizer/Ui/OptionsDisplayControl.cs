﻿// This file is Part of CalDavSynchronizer (http://outlookcaldavsynchronizer.sourceforge.net/)
// Copyright (c) 2015 Gerhard Zehetbauer 
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Scheduling;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Exception = System.Exception;

namespace CalDavSynchronizer.Ui
{
  public partial class OptionsDisplayControl : UserControl
  {
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);

    private OlItemType? _folderType;
    private string _folderEntryId;
    private string _folderStoreId;
    private readonly NameSpace _session;
    private Guid _optionsId;

    public event EventHandler DeletionRequested;
    public event EventHandler CopyRequested;
    public event EventHandler<HeaderEventArgs> HeaderChanged;

    private readonly IList<Item<int>> _availableSyncIntervals =
        (new Item<int>[] { new Item<int> (0, "Manual only") })
            .Union (Enumerable.Range (1, 12).Select (i => i * 5).Select (i => new Item<int> (i, i.ToString()))).ToList();

    private readonly IList<Item<ConflictResolution>> _availableConflictResolutions = new List<Item<ConflictResolution>>()
                                                                                     {
                                                                                         new Item<ConflictResolution> (ConflictResolution.OutlookWins, "OutlookWins"),
                                                                                         new Item<ConflictResolution> (ConflictResolution.ServerWins, "ServerWins"),
                                                                                         //new Item<ConflictResolution> (ConflictResolution.Manual, "Manual"),
                                                                                         new Item<ConflictResolution> (ConflictResolution.Automatic, "Automatic"),
                                                                                     };


    private readonly IList<Item<SynchronizationMode>> _availableSynchronizationModes = new List<Item<SynchronizationMode>>()
                                                                                       {
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.ReplicateOutlookIntoServer, "Outlook \u2192 CalDav (Replicate)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.ReplicateServerIntoOutlook, "Outlook \u2190 CalDav (Replicate)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeOutlookIntoServer, "Outlook \u2192 CalDav (Merge)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeServerIntoOutlook, "Outlook \u2190 CalDav (Merge)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeInBothDirections, "Outlook \u2190\u2192 CalDav"),
                                                                                       };


    private ServerAdapterType SelectedServerAdapterType
    {
      get { return _useGoogleOAuthCheckBox.Checked ? ServerAdapterType.GoogleOAuth : ServerAdapterType.Default; }
      set
      {
        switch (value)
        {
          case ServerAdapterType.Default:
            _useGoogleOAuthCheckBox.Checked = false;
            break;
          case ServerAdapterType.GoogleOAuth:
            _useGoogleOAuthCheckBox.Checked = true;
            break;
          default:
            throw new ArgumentOutOfRangeException ("value");
        }
      }
    }

    private void UpdatePasswordEnabled ()
    {
      _passwordTextBox.Enabled = SelectedServerAdapterType != ServerAdapterType.GoogleOAuth;
    }

    private void _useGoogleOAuthCheckBox_CheckedChanged (object sender, EventArgs e)
    {
      UpdatePasswordEnabled();
    }

    public OptionsDisplayControl (NameSpace session)
    {
      InitializeComponent();

      _session = session;
      BindComboBox (_syncIntervalComboBox, _availableSyncIntervals);
      BindComboBox (_conflictResolutionComboBox, _availableConflictResolutions);
      BindComboBox (_synchronizationModeComboBox, _availableSynchronizationModes);

      _testConnectionButton.Click += _testConnectionButton_Click;
      _selectOutlookFolderButton.Click += _selectOutlookFolderButton_Click;

      _profileNameTextBox.TextChanged += _profileNameTextBox_TextChanged;
      _synchronizationModeComboBox.SelectedValueChanged += _synchronizationModeComboBox_SelectedValueChanged;
      _inactiveCheckBox.CheckedChanged += _inactiveCheckBox_CheckedChanged;
    }

    public string ProfileName
    {
      get { return _profileNameTextBox.Text; }
    }

    private void _inactiveCheckBox_CheckedChanged (object sender, EventArgs e)
    {
      OnHeaderChanged();
    }

    private void _synchronizationModeComboBox_SelectedValueChanged (object sender, EventArgs e)
    {
      UpdateConflictResolutionComboBoxEnabled();
    }

    private void UpdateConflictResolutionComboBoxEnabled ()
    {
      switch ((SynchronizationMode) _synchronizationModeComboBox.SelectedValue)
      {
        case SynchronizationMode.MergeInBothDirections:
          _conflictResolutionComboBox.Enabled = true;
          break;
        default:
          _conflictResolutionComboBox.Enabled = false;
          break;
      }
    }


    private void _profileNameTextBox_TextChanged (object sender, EventArgs e)
    {
      OnHeaderChanged();
    }


    private void OnHeaderChanged ()
    {
      if (HeaderChanged != null)
      {
        var args = new HeaderEventArgs (_profileNameTextBox.Text, _inactiveCheckBox.Checked, _folderType);
        HeaderChanged (this, args);
      }
    }


    private void BindComboBox (ComboBox comboBox, IEnumerable list)
    {
      comboBox.DataSource = list;
      comboBox.ValueMember = "Value";
      comboBox.DisplayMember = "Name";
    }

    private async void _testConnectionButton_Click (object sender, EventArgs e)
    {
      await TestServerConnection();
    }

    private void _selectOutlookFolderButton_Click (object sender, EventArgs e)
    {
      SelectFolder();
    }

    private async Task TestServerConnection ()
    {
      const string connectionTestCaption = "Test settings";

      try
      {
        StringBuilder errorMessageBuilder = new StringBuilder();
        if (!ValidateCalendarUrl (errorMessageBuilder))
        {
          MessageBox.Show (errorMessageBuilder.ToString(), "The calendar Url is invalid", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }


        var calDavDataAccess = SynchronizerFactory.CreateCalDavDataAccess (
            _calenderUrlTextBox.Text,
            _userNameTextBox.Text,
            _passwordTextBox.Text,
            TimeSpan.Parse (ConfigurationManager.AppSettings["calDavConnectTimeout"]),
            SelectedServerAdapterType);

        var cardDavDataAccess = SynchronizerFactory.CreateCardDavDataAccess (
            _calenderUrlTextBox.Text,
            _userNameTextBox.Text,
            _passwordTextBox.Text,
            TimeSpan.Parse (ConfigurationManager.AppSettings["calDavConnectTimeout"]),
            SelectedServerAdapterType);

        var isCalendar = await calDavDataAccess.IsResourceCalender();
        var isAddressBook = await cardDavDataAccess.IsResourceAddressBook();

        if (!isCalendar && ! isAddressBook)
        {
          MessageBox.Show ("The specified Url is neither a calendar nor an addressbook!", connectionTestCaption);
          return;
        }

        if (isCalendar && isAddressBook)
        {
          MessageBox.Show ("Ressources which are a calendar and a addressbook are not valid!", connectionTestCaption);
          return;
        }

        bool hasError = false;
        errorMessageBuilder.Clear();

        if (isCalendar)
        {
          hasError = await TestCalendar (calDavDataAccess, errorMessageBuilder);
        }

        if (isAddressBook)
        {
          hasError = await TestAddressBook (cardDavDataAccess, errorMessageBuilder);
        }

        if (hasError)
          MessageBox.Show ("Connection test NOT successful:" + Environment.NewLine + errorMessageBuilder.ToString(), connectionTestCaption);
        else
          MessageBox.Show ("Connection test successful.", connectionTestCaption);
      }
      catch (Exception x)
      {
        MessageBox.Show (x.Message, connectionTestCaption);
      }
    }

    private async Task<bool> TestAddressBook (CardDavDataAccess cardDavDataAccess, StringBuilder errorMessageBuilder)
    {
      bool hasError = false;
      if (!await cardDavDataAccess.IsAddressBookAccessSupported())
      {
        errorMessageBuilder.AppendLine ("- The specified Url does not support addressbook.");
        hasError = true;
      }

      if (!await cardDavDataAccess.IsWriteable())
      {
        errorMessageBuilder.AppendLine ("- The specified Url is a read-only addressbook.");
        hasError = true;
      }

      if (_folderType != OlItemType.olContactItem)
      {
        errorMessageBuilder.AppendLine ("- The outlook folder is not a address book, or there is no folder selected.");
        hasError = true;
      }
      return hasError;
    }

    private async Task<bool> TestCalendar (CalDavDataAccess calDavDataAccess, StringBuilder errorMessageBuilder)
    {
      bool hasError = false;

      if (!await calDavDataAccess.IsCalendarAccessSupported())
      {
        errorMessageBuilder.AppendLine ("- The specified Url does not support calendar access.");
        hasError = true;
      }

      if (!await calDavDataAccess.IsWriteable())
      {
        errorMessageBuilder.AppendLine ("- The specified Url is a read-only calendar.");
        hasError = true;
      }

      if (!await calDavDataAccess.DoesSupportCalendarQuery())
      {
        errorMessageBuilder.AppendLine ("- The specified Url does not support Calendar Queries.");
        hasError = true;
      }

      if (_folderType != OlItemType.olAppointmentItem && _folderType != OlItemType.olTaskItem)
      {
        errorMessageBuilder.AppendLine ("- The outlook folder is not a calendar or task folder, or there is no folder selected.");
        hasError = true;
      }

      return hasError;
    }

    public bool Validate (StringBuilder errorMessageBuilder)
    {
      bool result = ValidateCalendarUrl (errorMessageBuilder);

      if (string.IsNullOrWhiteSpace (_folderStoreId) || string.IsNullOrWhiteSpace (_folderEntryId))
      {
        errorMessageBuilder.AppendLine ("- There is no Outlook Folder selected.");
        result = false;
      }

      try
      {
        var uri = new Uri ("mailto:" + _emailAddressTextBox.Text).ToString();
      }
      catch (Exception x)
      {
        errorMessageBuilder.AppendFormat ("- The Email Address is invalid. ({0})", x.Message);
        errorMessageBuilder.AppendLine();
        result = false;
      }

      return result;
    }

    private bool ValidateCalendarUrl (StringBuilder errorMessageBuilder)
    {
      bool result = true;

      if (string.IsNullOrWhiteSpace (_calenderUrlTextBox.Text))
      {
        errorMessageBuilder.AppendLine ("- The CalDav Calendar Url is empty.");
        return false;
      }

      if (_calenderUrlTextBox.Text.Trim() != _calenderUrlTextBox.Text)
      {
        errorMessageBuilder.AppendLine ("- The CalDav Calendar Url cannot end/start with whitespaces.");
        result = false;
      }

      if (!_calenderUrlTextBox.Text.EndsWith ("/"))
      {
        errorMessageBuilder.AppendLine ("- The CalDav Calendar Url hast to end with an slash ('/').");
        result = false;
      }

      try
      {
        var uri = new Uri (_calenderUrlTextBox.Text).ToString();
      }
      catch (Exception x)
      {
        errorMessageBuilder.AppendFormat ("- The CalDav Calendar Url is not a well formed Url. ({0})", x.Message);
        errorMessageBuilder.AppendLine();
        result = false;
      }

      return result;
    }


    public Options Options
    {
      set
      {
        _inactiveCheckBox.Checked = value.Inactive;
        _profileNameTextBox.Text = value.Name;
        numberOfDaysInThePast.Text = value.DaysToSynchronizeInThePast.ToString();
        numberOfDaysInTheFuture.Text = value.DaysToSynchronizeInTheFuture.ToString();

        _emailAddressTextBox.Text = value.EmailAddress;
        _calenderUrlTextBox.Text = value.CalenderUrl;
        _userNameTextBox.Text = value.UserName;
        _passwordTextBox.Text = value.Password;

        _synchronizationModeComboBox.SelectedValue = value.SynchronizationMode;
        _conflictResolutionComboBox.SelectedValue = value.ConflictResolution;

        _enableTimeRangeFilteringCheckBox.Checked = !value.IgnoreSynchronizationTimeRange;
        _syncIntervalComboBox.SelectedValue = value.SynchronizationIntervalInMinutes;
        _optionsId = value.Id;

        SelectedServerAdapterType = value.ServerAdapterType;

        UpdateFolder (value.OutlookFolderEntryId, value.OutlookFolderStoreId);
        UpdateConflictResolutionComboBoxEnabled();
        OnHeaderChanged();
        UpdateTimeRangeFilteringGroupBoxEnabled();
      }
      get
      {
        return new Options()
               {
                   Name = _profileNameTextBox.Text,
                   DaysToSynchronizeInThePast = int.Parse (numberOfDaysInThePast.Text),
                   DaysToSynchronizeInTheFuture = int.Parse (numberOfDaysInTheFuture.Text),
                   EmailAddress = _emailAddressTextBox.Text,
                   CalenderUrl = _calenderUrlTextBox.Text,
                   UserName = _userNameTextBox.Text,
                   Password = _passwordTextBox.Text,
                   SynchronizationMode = (SynchronizationMode) _synchronizationModeComboBox.SelectedValue,
                   ConflictResolution = (ConflictResolution) (_conflictResolutionComboBox.SelectedValue ?? ConflictResolution.Manual),
                   SynchronizationIntervalInMinutes = (int) _syncIntervalComboBox.SelectedValue,
                   OutlookFolderEntryId = _folderEntryId,
                   OutlookFolderStoreId = _folderStoreId,
                   Id = _optionsId,
                   Inactive = _inactiveCheckBox.Checked,
                   IgnoreSynchronizationTimeRange = !_enableTimeRangeFilteringCheckBox.Checked,
                   ServerAdapterType = SelectedServerAdapterType
               };
      }
    }


    private bool IsTaskSynchronizationEnabled
    {
      get
      {
        bool enabled;
        if (bool.TryParse (ConfigurationManager.AppSettings["enableTaskSynchronization"], out enabled))
          return enabled;
        else
          return false;
      }
    }

    private void UpdateFolder (MAPIFolder folder)
    {
      if (IsTaskSynchronizationEnabled)
      {
        if (folder.DefaultItemType != OlItemType.olAppointmentItem && folder.DefaultItemType != OlItemType.olTaskItem && folder.DefaultItemType != OlItemType.olContactItem)
        {
          string wrongFolderMessage = string.Format ("Wrong ItemType in folder '{0}'. It should be a calendar, task or contact folder.", folder.Name);
          MessageBox.Show (wrongFolderMessage, "Configuration Error");
          return;
        }
      }
      else
      {
        if (folder.DefaultItemType != OlItemType.olAppointmentItem && folder.DefaultItemType != OlItemType.olContactItem)
        {
          string wrongFolderMessage = string.Format ("Wrong ItemType in folder '{0}'. It should be a calendar or contact folder.", folder.Name);
          MessageBox.Show (wrongFolderMessage, "Configuration Error");
          return;
        }
      }

      _folderEntryId = folder.EntryID;
      _folderStoreId = folder.StoreID;
      _outoookFolderNameTextBox.Text = folder.Name;
      _folderType = folder.DefaultItemType;
      OnHeaderChanged();
    }

    private void UpdateFolder (string folderEntryId, string folderStoreId)
    {
      if (!string.IsNullOrEmpty (folderEntryId) && !string.IsNullOrEmpty (folderStoreId))
      {
        GenericComObjectWrapper<MAPIFolder> folderWrapper = null;

        try
        {
          try
          {
            folderWrapper = GenericComObjectWrapper.Create (_session.GetFolderFromID (folderEntryId, folderStoreId));
          }
          catch (Exception x)
          {
            s_logger.Error (null, x);
            _outoookFolderNameTextBox.Text = "<ERROR>";
            _folderType = null;
            return;
          }
          if (folderWrapper != null)
          {
            UpdateFolder (folderWrapper.Inner);
            return;
          }
        }
        finally
        {
          if (folderWrapper != null)
            folderWrapper.Dispose();
        }
      }

      _outoookFolderNameTextBox.Text = "<MISSING>";
      _folderType = null;
    }

    private void SelectFolder ()
    {
      var folder = _session.PickFolder();
      if (folder != null)
      {
        using (var folderWrapper = GenericComObjectWrapper.Create (folder))
        {
          UpdateFolder (folderWrapper.Inner);
        }
      }

      if (_folderType == OlItemType.olContactItem)
      {
        MessageBox.Show (
            "The contact synchronization is currently in development and has limited functionality.",
            "CalDav Synchronizer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
      }
    }

    private void _deleteButton_Click (object sender, EventArgs e)
    {
      if (DeletionRequested != null)
        DeletionRequested (this, EventArgs.Empty);
    }

    private void _copyButton_Click (object sender, EventArgs e)
    {
      if (CopyRequested != null)
        CopyRequested (this, EventArgs.Empty);
    }

    private void _enableTimeRangeFilteringCheckBox_CheckedChanged (object sender, EventArgs e)
    {
      UpdateTimeRangeFilteringGroupBoxEnabled();
    }

    private void UpdateTimeRangeFilteringGroupBoxEnabled ()
    {
      _timeRangeFilteringGroupBox.Enabled = _enableTimeRangeFilteringCheckBox.Checked;
    }
  }
}