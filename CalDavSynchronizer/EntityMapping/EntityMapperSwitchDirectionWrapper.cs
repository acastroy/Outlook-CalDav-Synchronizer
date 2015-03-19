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

namespace CalDavSynchronizer.EntityMapping
{
  internal class EntityMapperSwitchDirectionWrapper<T1, T2> : IEntityMapper<T1, T2>
  {
    private readonly IEntityMapper<T2, T1> _inner;

    public EntityMapperSwitchDirectionWrapper (IEntityMapper<T2, T1> inner)
    {
      _inner = inner;
    }

    public T2 Map1To2 (T1 source, T2 target)
    {
      return _inner.Map2To1 (source, target);
    }

    public T1 Map2To1 (T2 source, T1 target)
    {
      return _inner.Map1To2 (source, target);
    }
  }
}