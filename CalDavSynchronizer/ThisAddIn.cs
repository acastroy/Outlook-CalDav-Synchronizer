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
using Microsoft.Office.Tools.Ribbon;
using Office = Microsoft.Office.Core;

namespace CalDavSynchronizer
{
  public partial class ThisAddIn
  {
    public static ComponentContainer ComponentContainer { get; private set; }

    private void ThisAddIn_Startup (object sender, EventArgs e)
    {
      ComponentContainer.ConfigureServicePointManager();
      ComponentContainer = new ComponentContainer (Application.Session);
    }

    private void ThisAddIn_Shutdown (object sender, EventArgs e)
    {
    }

    protected override IRibbonExtension[] CreateRibbonObjects ()
    {
      return new IRibbonExtension[] { new CalDavSynchronizerRibbon() };
    }

    #region VSTO generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InternalStartup ()
    {
      Startup += ThisAddIn_Startup;
      Shutdown += ThisAddIn_Shutdown;
    }

    #endregion
  }
}