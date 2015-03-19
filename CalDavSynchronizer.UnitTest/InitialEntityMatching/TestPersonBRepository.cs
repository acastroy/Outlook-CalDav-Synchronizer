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
using System.Linq;
using CalDavSynchronizer.EntityRepositories;
using CalDavSynchronizer.EntityVersionManagement;

namespace CalDavSynchronizer.UnitTest.InitialEntityMatching
{
  internal class TestPersonBRepository : EntityRepositoryBase<PersonB, string, string>
  {
    private readonly IEnumerable<PersonB> _persons;

    public TestPersonBRepository (IEnumerable<PersonB> persons)
    {
      _persons = persons;
    }


    public override IEnumerable<EntityIdWithVersion<string, string>> GetEntityVersions (DateTime @from, DateTime to)
    {
      return _persons.Select (p => new EntityIdWithVersion<string, string> (p.Id, p.Version)).ToArray();
    }

    public override IDictionary<string, PersonB> GetEntities (ICollection<string> sourceEntityIds)
    {
      var personsById = _persons.ToDictionary (p => p.Id);
      return sourceEntityIds.Select (id => personsById[id]).ToDictionary (p => p.Id);
    }

    public override bool Delete (string entityId)
    {
      throw new NotImplementedException();
    }

    public override EntityIdWithVersion<string, string> Update (string entityId, Func<PersonB, PersonB> entityModifier, PersonB cachedCurrentTargetEntityIfAvailable)
    {
      throw new NotImplementedException();
    }

    public override EntityIdWithVersion<string, string> Create (Func<PersonB, PersonB> entityInitializer)
    {
      throw new NotImplementedException();
    }
  }
}