using Sitecore.Configuration;
using Sitecore.Data.Events;
using Sitecore.Diagnostics;
using Sitecore.Events;
using Sitecore.Security;
using Sitecore.Security.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Security;

namespace Sitecore.Support.Security
{
    public class SitecoreMembershipProvider : Sitecore.Security.SitecoreMembershipProvider
    {
        public override void UpdateUser(MembershipUser user)
        {
            Factory.GetRetryer().ExecuteNoResult(() => this.RealProvider.UpdateUser(user));
            this.RaiseEvent("user:updated", new object[] { user });
        }


        private void RaiseEvent(string eventName, params object[] parameters)
        {
            if (!EventDisabler.IsActive)
            {
                Event.RaiseEvent(eventName, parameters);
            }
        }

        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status)
        {
            MembershipCreateStatus operationStatus = MembershipCreateStatus.Success;
            MembershipUser user = Factory.GetRetryer().Execute<MembershipUser>(() => this.RealProvider.CreateUser(username, password, email, passwordQuestion, passwordAnswer, isApproved, providerUserKey, out operationStatus));
            status = operationStatus;
            if (user == null)
            {
                return null;
            }
            this.RaiseEvent("user:created", new object[] { user });
            user = this.Wrap(user);
            if (user != null)
            {
                UpdateDigestCredentials(username, password);
            }
            return user;
        }

        internal static bool UpdateDigestCredentials(string username, string password)
        {
            Assert.IsNotNull(username, "user name");
            Assert.IsNotNull(password, "password");
            string str = SecurityUtil.ComputeDigestCredentials(username, password);
            string str2 = str;
            if (username.Contains(@"\") && (username.IndexOf(@"\", StringComparison.InvariantCulture) < username.Length))
            {
                str2 = SecurityUtil.ComputeDigestCredentials(username.Substring(username.IndexOf(@"\", StringComparison.InvariantCulture) + 1), password);
            }
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(str2))
            {
                return false;
            }
            User user = User.FromName(username, true);
            if (user == null)
            {
                return false;
            }
            string userDigestCredentials = SecurityUtil.GetUserDigestCredentials(user, false);
            string strA = SecurityUtil.GetUserDigestCredentials(user, true);
            return (((string.Compare(userDigestCredentials, str, StringComparison.InvariantCulture) == 0) && (string.Compare(strA, str2, StringComparison.InvariantCulture) == 0)) || SetUserDigestCredentials(user, str, str2));
        }

        internal static bool SetUserDigestCredentials(User user, string credentials, string credentialsWithoutDomain)
        {
            Assert.IsNotNull(user, "user");
            Assert.IsNotNull(credentials, "credentials");
            UserProfile profile = user.Profile;
            if (profile == null)
            {
                return false;
            }
            profile.SetCustomProperty(DigestPropertyName, credentials);
            profile.SetCustomProperty(DigestPropertyNameWithoutDomain, credentialsWithoutDomain);
            profile.Save();
            return true;
        }

        // Properties
        private static string DigestPropertyName
        {
            get
            {
                return "digestcredentialhash";
            }
        }

        private static string DigestPropertyNameWithoutDomain
        {
            get
            {
                return "digestcredentialhashwithoutdomain";
            }
        }



        private MembershipUser Wrap(MembershipUser user)
        {
            if (user == null)
            {
                return null;
            }
            if (user is MembershipUserWrapper)
            {
                return user;
            }
            return new MembershipUserWrapper(user, this.Name);
        }

    }
}
