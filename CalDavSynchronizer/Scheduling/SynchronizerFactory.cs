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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Contacts;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.Tasks;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using CalDavSynchronizer.Utilities;
using DDay.iCal;
using DDay.iCal.Serialization.iCalendar;
using GenSync.EntityRelationManagement;
using GenSync.EntityRepositories;
using GenSync.ProgressReport;
using GenSync.Synchronization;
using GenSync.Synchronization.StateFactories;
using Microsoft.Office.Interop.Outlook;
using Thought.vCards;

namespace CalDavSynchronizer.Scheduling
{
  public class SynchronizerFactory : ISynchronizerFactory
  {
    private readonly string _outlookEmailAddress;
    private readonly string _applicationDataDirectory;
    private readonly ITotalProgressFactory _totalProgressFactory;
    private readonly NameSpace _outlookSession;
    private readonly TimeSpan _calDavConnectTimeout;
    private readonly TimeSpan _calDavReadWriteTimeout;

    public SynchronizerFactory (
        string applicationDataDirectory,
        ITotalProgressFactory totalProgressFactory,
        NameSpace outlookSession,
        TimeSpan calDavConnectTimeout,
        TimeSpan calDavReadWriteTimeout)
    {
      _outlookEmailAddress = outlookSession.CurrentUser.Address;
      _applicationDataDirectory = applicationDataDirectory;
      _totalProgressFactory = totalProgressFactory;
      _outlookSession = outlookSession;
      _calDavConnectTimeout = calDavConnectTimeout;
      _calDavReadWriteTimeout = calDavReadWriteTimeout;
    }

    public ISynchronizer CreateSynchronizer (Options options)
    {
      OlItemType defaultItemType;
      string folderName;

      using (var outlookFolderWrapper = GenericComObjectWrapper.Create ((Folder) _outlookSession.GetFolderFromID (options.OutlookFolderEntryId, options.OutlookFolderStoreId)))
      {
        defaultItemType = outlookFolderWrapper.Inner.DefaultItemType;
        folderName = outlookFolderWrapper.Inner.Name;
      }

      switch (defaultItemType)
      {
        case OlItemType.olAppointmentItem:
          return CreateEventSynchronizer (options);
        case OlItemType.olTaskItem:
          return CreateTaskSynchronizer (options);
        case OlItemType.olContactItem:
          return CreateContactSynchronizer (options);
        default:
          throw new NotSupportedException (
              string.Format (
                  "The folder '{0}' contains an item type ('{1}'), whis is not supported for synchronization",
                  folderName,
                  defaultItemType));
      }
    }

    private ISynchronizer CreateEventSynchronizer (Options options)
    {
      var calDavDataAccess = CreateCalDavDataAccess (
          options.CalenderUrl,
          options.UserName,
          options.Password,
          _calDavConnectTimeout,
          options.ServerAdapterType);


      var storageDataDirectory = Path.Combine (
          _applicationDataDirectory,
          options.Id.ToString()
          );

      var entityRelationDataAccess = new EntityRelationDataAccess<string, DateTime, OutlookEventRelationData, Uri, string> (storageDataDirectory);

      return CreateEventSynchronizer (options, calDavDataAccess, entityRelationDataAccess);
    }

    public static CalDavDataAccess CreateCalDavDataAccess (
        string calenderUrl,
        string username,
        string password,
        TimeSpan timeout,
        ServerAdapterType serverAdapterType)
    {
      var productAndVersion = GetProductAndVersion();

      var calDavDataAccess = new CalDavDataAccess (
          new Uri (calenderUrl),
          new CalDavClient (
              () => CreateHttpClient (username, password, timeout, serverAdapterType),
              productAndVersion.Item1,
              productAndVersion.Item2));
      return calDavDataAccess;
    }

    public static CardDavDataAccess CreateCardDavDataAccess (
        string calenderUrl,
        string username,
        string password,
        TimeSpan timeout,
        ServerAdapterType serverAdapterType)
    {
      var productAndVersion = GetProductAndVersion();

      var cardDavDataAccess = new CardDavDataAccess (
          new Uri (calenderUrl),
          new CardDavClient (
              () => CreateHttpClient (username, password, timeout, serverAdapterType),
              productAndVersion.Item1,
              productAndVersion.Item2));
      return cardDavDataAccess;
    }


    private static async Task<HttpClient> CreateHttpClient (string username, string password, TimeSpan calDavConnectTimeout, ServerAdapterType serverAdapterType)
    {
      switch (serverAdapterType)
      {
        case ServerAdapterType.Default:
          var httpClientHandler = new HttpClientHandler();
          if (!string.IsNullOrEmpty (username))
          {
            httpClientHandler.Credentials = new NetworkCredential (username, password);
          }
          var httpClient = new HttpClient (httpClientHandler);
          httpClient.Timeout = calDavConnectTimeout;
          return httpClient;
        case ServerAdapterType.GoogleOAuth:
          return await OAuth.Google.GoogleHttpClientFactory.CreateHttpClient (username, GetProductWithVersion());
        default:
          throw new ArgumentOutOfRangeException ("serverAdapterType");
      }
    }

    private static Tuple<string, string> GetProductAndVersion ()
    {
      var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
      return Tuple.Create ("CalDavSynchronizer", string.Format ("{0}.{1}", version.Major, version.Minor));
    }

    private static string GetProductWithVersion ()
    {
      var productAndVersion = GetProductAndVersion();
      return string.Format ("{0}/{1}", productAndVersion.Item1, productAndVersion.Item2);
    }

    /// <remarks>
    /// Public because it is being used by integration tests
    /// </remarks>
    public ISynchronizer CreateEventSynchronizer (
        Options options,
        ICalDavDataAccess calDavDataAccess,
        IEntityRelationDataAccess<string, DateTime, Uri, string> entityRelationDataAccess)
    {
      var dateTimeRangeProvider =
          options.IgnoreSynchronizationTimeRange ?
              NullDateTimeRangeProvider.Instance :
              new DateTimeRangeProvider (options.DaysToSynchronizeInThePast, options.DaysToSynchronizeInTheFuture);

      var atypeRepository = new OutlookEventRepository (
          _outlookSession,
          options.OutlookFolderEntryId,
          options.OutlookFolderStoreId,
          dateTimeRangeProvider);

      IEntityRepository<IICalendar, Uri, string> btypeRepository = new CalDavRepository (
          calDavDataAccess,
          new iCalendarSerializer(),
          CalDavRepository.EntityType.Event,
          dateTimeRangeProvider);

      var entityMapper = new EventEntityMapper (
          _outlookEmailAddress, new Uri ("mailto:" + options.EmailAddress),
          _outlookSession.Application.TimeZones.CurrentTimeZone.ID,
          _outlookSession.Application.Version);

      var outlookEventRelationDataFactory = new OutlookEventRelationDataFactory();

      var syncStateFactory = new EntitySyncStateFactory<string, DateTime, AppointmentItemWrapper, Uri, string, IICalendar> (
          entityMapper,
          atypeRepository,
          btypeRepository,
          outlookEventRelationDataFactory,
          ExceptionHandler.Instance
          );


      var btypeIdEqualityComparer = EqualityComparer<Uri>.Default;
      var atypeIdEqualityComparer = EqualityComparer<string>.Default;

      return new Synchronizer<string, DateTime, AppointmentItemWrapper, Uri, string, IICalendar> (
          atypeRepository,
          btypeRepository,
          InitialEventSyncStateCreationStrategyFactory.Create (
              syncStateFactory,
              syncStateFactory.Environment,
              options.SynchronizationMode,
              options.ConflictResolution),
          entityRelationDataAccess,
          outlookEventRelationDataFactory,
          new InitialEventEntityMatcher (btypeIdEqualityComparer),
          atypeIdEqualityComparer,
          btypeIdEqualityComparer,
          _totalProgressFactory,
          ExceptionHandler.Instance);
    }

    private ISynchronizer CreateTaskSynchronizer (Options options)
    {
      // TODO: dispose folder like it is done for events
      var calendarFolder = (Folder) _outlookSession.GetFolderFromID (options.OutlookFolderEntryId, options.OutlookFolderStoreId);
      var atypeRepository = new OutlookTaskRepository (calendarFolder, _outlookSession);

      var btypeRepository = new CalDavRepository (
          CreateCalDavDataAccess (
              options.CalenderUrl,
              options.UserName,
              options.Password,
              _calDavConnectTimeout,
              options.ServerAdapterType),
          new iCalendarSerializer(),
          CalDavRepository.EntityType.Todo,
          NullDateTimeRangeProvider.Instance);

      var outlookEventRelationDataFactory = new OutlookEventRelationDataFactory();
      var syncStateFactory = new EntitySyncStateFactory<string, DateTime, TaskItemWrapper, Uri, string, IICalendar> (
          new TaskMapper (_outlookSession.Application.TimeZones.CurrentTimeZone.ID),
          atypeRepository,
          btypeRepository,
          outlookEventRelationDataFactory,
          ExceptionHandler.Instance);

      var storageDataDirectory = Path.Combine (
          _applicationDataDirectory,
          options.Id.ToString()
          );

      var btypeIdEqualityComparer = EqualityComparer<Uri>.Default;
      var atypeIdEqualityComparer = EqualityComparer<string>.Default;

      return new Synchronizer<string, DateTime, TaskItemWrapper, Uri, string, IICalendar> (
          atypeRepository,
          btypeRepository,
          InitialTaskSyncStateCreationStrategyFactory.Create (
              syncStateFactory,
              syncStateFactory.Environment,
              options.SynchronizationMode,
              options.ConflictResolution),
          new EntityRelationDataAccess<string, DateTime, OutlookEventRelationData, Uri, string> (storageDataDirectory),
          outlookEventRelationDataFactory,
          new InitialTaskEntityMatcher (btypeIdEqualityComparer),
          atypeIdEqualityComparer,
          btypeIdEqualityComparer,
          _totalProgressFactory,
          ExceptionHandler.Instance);
    }

    private ISynchronizer CreateContactSynchronizer (Options options)
    {
      var atypeRepository = new OutlookContactRepository (
          _outlookSession,
          options.OutlookFolderEntryId,
          options.OutlookFolderStoreId);


      IEntityRepository<vCard, Uri, string> btypeRepository = new CardDavRepository (
          CreateCardDavDataAccess (
              options.CalenderUrl,
              options.UserName,
              options.Password,
              _calDavConnectTimeout,
              options.ServerAdapterType));

      var entityRelationDataFactory = new OutlookContactRelationDataFactory();

      var syncStateFactory = new EntitySyncStateFactory<string, DateTime, GenericComObjectWrapper<ContactItem>, Uri, string, vCard> (
          new ContactEntityMapper(),
          atypeRepository,
          btypeRepository,
          entityRelationDataFactory,
          ExceptionHandler.Instance);

      var btypeIdEqualityComparer = EqualityComparer<Uri>.Default;
      var atypeIdEqulityComparer = EqualityComparer<string>.Default;

      var storageDataDirectory = Path.Combine (
          _applicationDataDirectory,
          options.Id.ToString()
          );

      var storageDataAccess = new EntityRelationDataAccess<string, DateTime, OutlookContactRelationData, Uri, string> (storageDataDirectory);

      return new Synchronizer<string, DateTime, GenericComObjectWrapper<ContactItem>, Uri, string, vCard> (
          atypeRepository,
          btypeRepository,
          InitialContactSyncStateCreationStrategyFactory.Create (
              syncStateFactory,
              syncStateFactory.Environment,
              options.SynchronizationMode,
              options.ConflictResolution),
          storageDataAccess,
          entityRelationDataFactory,
          new InitialContactEntityMatcher (btypeIdEqualityComparer),
          atypeIdEqulityComparer,
          btypeIdEqualityComparer,
          _totalProgressFactory,
          ExceptionHandler.Instance);
    }
  }
}