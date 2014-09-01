using Sitecore.Configuration;
using Sitecore.Data.Events;
using Sitecore.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sitecore.Support.Security
{
    public class SitecoreRoleProvider : Sitecore.Security.SitecoreRoleProvider
    {
        public override void AddUsersToRoles(string[] userNames, string[] roleNames)
        {
            Factory.GetRetryer().ExecuteNoResult(() => this.RealProvider.AddUsersToRoles(userNames, roleNames));
            this.RaiseEvent("roles:usersAdded", new object[] { userNames, roleNames });
        }

        private void RaiseEvent(string eventName, params object[] parameters)
        {
            if (!EventDisabler.IsActive)
            {
                Event.RaiseEvent(eventName, parameters);
            }         
        }

    }
}
