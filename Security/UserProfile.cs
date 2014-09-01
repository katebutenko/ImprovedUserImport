using Sitecore.Caching;
using Sitecore.Caching.UserProfile;
using Sitecore.Common;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Events;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Events;
using Sitecore.Globalization;
using Sitecore.Security.Accounts;
using Sitecore.Security.Domains;
using Sitecore.SecurityModel;
using Sitecore.StringExtensions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Profile;
using System.Web.Security;

namespace Sitecore.Support.Security
{
    public class ExtUserProfile : Sitecore.Security.UserProfile
    {
        // Fields
        private string comment;
        private Dictionary<string, string> customProperties;
        private bool customPropertiesIsDirty;
        private string email;
        private bool emailChanged;
        private Reference<MembershipUser> innerMembershipUser;
        private User profileUser;
        private readonly object propertiesLock = new object();

        // Methods
        private CultureInfo GetCulture()
        {
            string regionalIsoCode = this.RegionalIsoCode;
            if (regionalIsoCode.Length > 0)
            {
                return Language.CreateCultureInfo(regionalIsoCode, true);
            }
            return Sitecore.Context.Language.CultureInfo;
        }

        public virtual string GetCustomProperty(string propertyName)
        {
            string str;
            Assert.ArgumentNotNull(propertyName, "propertyName");
            this.CustomProperties.TryGetValue(propertyName, out str);
            if (str != null)
            {
                return str;
            }
            return this.GetPropertyValueFromProfileItem(propertyName);
        }

        public virtual List<string> GetCustomPropertyNames()
        {
            List<string> list = new List<string>();
            Dictionary<string, string> customProperties = this.CustomProperties;
            lock (customProperties)
            {
                foreach (string str in customProperties.Keys)
                {
                    list.Add(str);
                }
            }
            return list;
        }

        protected virtual Item GetProfileItem()
        {
            ID id;
            string profileItemId = this.ProfileItemId;
            if (string.IsNullOrEmpty(profileItemId))
            {
                return null;
            }
            if (!ID.TryParse(profileItemId, out id))
            {
                return null;
            }
            using (new SecurityDisabler())
            {
                Database database = Factory.GetDatabase(Settings.ProfileItemDatabase, false);
                if (database == null)
                {
                    Log.SingleWarn("Cannot retrieve user profile item. Profile item database '{0}' was not found".FormatWith(new object[] { Settings.ProfileItemDatabase }), this);
                    return null;
                }
                return database.GetItem(id);
            }
        }

        protected virtual object GetPropertyValueCore(string propertyName)
        {
            object propertyValueFromProfileItem;
            Func<object> action = null;
            Assert.ArgumentNotNullOrEmpty(propertyName, "propertyName");
            UserProfileCacheRecord record = this.Cache.GetRecord(base.UserName, propertyName);
            if (record != null)
            {
                return record.Value;
            }
            try
            {
                if (this.IsUserVirtual())
                {
                    propertyValueFromProfileItem = this.ProfileUser.RuntimeSettings.Properties[propertyName];
                }
                else
                {
                    if (action == null)
                    {
                        action = () => this.GetPropertyValue(propertyName);
                    }
                    propertyValueFromProfileItem = Factory.GetRetryer().Execute<object>(action);
                }
            }
            catch
            {
                propertyValueFromProfileItem = null;
            }
            if (((propertyValueFromProfileItem == null) || ((propertyValueFromProfileItem is string) && (propertyValueFromProfileItem.ToString() == string.Empty))) && !propertyName.Equals("ProfileItemId", StringComparison.OrdinalIgnoreCase))
            {
                propertyValueFromProfileItem = this.GetPropertyValueFromProfileItem(propertyName);
            }
            this.Cache.AddRecord(base.UserName, propertyName, propertyValueFromProfileItem);
            return propertyValueFromProfileItem;
        }

        private string GetPropertyValueFromProfileItem(string propertyName)
        {
            Assert.ArgumentNotNull(propertyName, "propertyName");
            Item profileItem = this.GetProfileItem();
            if (profileItem == null)
            {
                return string.Empty;
            }
            Field field = profileItem.Fields[propertyName];
            if (field == null)
            {
                return string.Empty;
            }
            return field.Value;
        }

        private MembershipUser GetUser()
        {
            MembershipUser user = null;
            if (!base.UserName.Contains(","))
            {
                user = Membership.GetUser(base.UserName);
            }
            return user;
        }

        private static bool IsCoreProperty(string propertyName)
        {
            Assert.ArgumentNotNull(propertyName, "propertyName");
            foreach (SettingsProperty property in ProfileBase.Properties)
            {
                if (string.Compare(property.Name, propertyName, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsUserVirtual()
        {
            User profileUser = this.ProfileUser;
            return ((profileUser != null) && profileUser.RuntimeSettings.IsVirtual);
        }

        private void RaiseUserUpdatedEvent()
        {
            MembershipUser user = Membership.GetUser(base.UserName);
            if (user != null)
            {
                if (!EventDisabler.IsActive)
                {
                    Event.RaiseEvent("user:updated", new object[] { user });
                }
            }
        }

        public virtual void Reload()
        {
            this.Cache.RemoveUser(base.UserName);
            this.customProperties = null;
            this.customPropertiesIsDirty = false;
            this.innerMembershipUser = null;
        }

        public virtual void RemoveCustomProperty(string propertyName)
        {
            Assert.ArgumentNotNullOrEmpty(propertyName, "propertyName");
            Dictionary<string, string> customProperties = this.CustomProperties;
            lock (customProperties)
            {
                customProperties.Remove(propertyName);
            }
            this.customPropertiesIsDirty = true;
        }

        public override void Save()
        {
            this.innerMembershipUser = null;
            bool flag = (this.ProfileUser == null) || !this.ProfileUser.RuntimeSettings.IsVirtual;
            if (flag)
            {
                this.SaveUserProperties();
            }
            User profileUser = this.ProfileUser;
            if (profileUser != null)
            {
                profileUser.RuntimeSettings.Serialize();
            }
            this.SerializeCustomProperties();
            if (flag)
            {
                base.Save();
            }
            this.RaiseUserUpdatedEvent();
        }

        private void SaveUserProperties()
        {
            if (((this.ProfileUser == null) || !this.ProfileUser.RuntimeSettings.IsVirtual) && this.emailChanged)
            {
                MembershipUser user = Membership.GetUser(base.UserName);
                if (user != null)
                {
                    user.Email = this.email;
                    Membership.UpdateUser(user);
                }
            }
        }

        private void SerializeCustomProperties()
        {
            if (this.customPropertiesIsDirty)
            {
                this.SerializedData = this.CustomProperties;
            }
        }

        public virtual void SetCustomProperty(string propertyName, string value)
        {
            Assert.ArgumentNotNull(propertyName, "propertyName");
            Assert.ArgumentNotNull(value, "value");
            Dictionary<string, string> customProperties = this.CustomProperties;
            lock (customProperties)
            {
                customProperties[propertyName] = value;
            }
            this.customPropertiesIsDirty = true;
        }

        protected virtual void SetPropertyValueCore(string propertyName, object value)
        {
            Assert.ArgumentNotNullOrEmpty(propertyName, "propertyName");
            if (this.IsUserVirtual())
            {
                this.ProfileUser.RuntimeSettings.Properties[propertyName] = value;
            }
            else
            {
                Factory.GetRetryer().ExecuteNoResult(() => this.SetPropertyValue(propertyName, value));
                this.SetPropertyValueToCache(propertyName, value);
            }
        }

        private void SetPropertyValueToCache(string propertyName, object propertyValue)
        {
            Assert.IsNotNullOrEmpty(propertyName, "propertyName");
            UserProfileCache userProfileCache = CacheManager.GetUserProfileCache();
            Assert.IsNotNull(userProfileCache, "profileCache");
            userProfileCache.AddRecord(base.UserName, propertyName, propertyValue);
        }

        // Properties
        [Obsolete]
        public virtual string AssignableRoles
        {
            get
            {
                return (this.GetPropertyValueCore("AssignableRoles") as string);
            }
            set
            {
                this.SetPropertyValueCore("AssignableRoles", value);
            }
        }

        private UserProfileCache Cache
        {
            get
            {
                UserProfileCache userProfileCache = CacheManager.GetUserProfileCache();
                Assert.IsNotNull(userProfileCache, "profileCache");
                return userProfileCache;
            }
        }

        public virtual string ClientLanguage
        {
            get
            {
                return (this.GetPropertyValueCore("ClientLanguage") as string);
            }
            set
            {
                this.SetPropertyValueCore("ClientLanguage", value);
            }
        }

        public virtual string Comment
        {
            get
            {
                string comment = this.comment;
                if (comment == null)
                {
                    if (this.IsUserVirtual())
                    {
                        return (this.GetPropertyValueCore("Comment") as string);
                    }
                    MembershipUser membershipUser = this.MembershipUser;
                    if (membershipUser == null)
                    {
                        return null;
                    }
                    comment = membershipUser.Comment;
                    this.comment = comment;
                }
                return comment;
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                if (this.IsUserVirtual())
                {
                    this.SetPropertyValueCore("Comment", value);
                }
                else
                {
                    MembershipUser user = Membership.GetUser(base.UserName);
                    Assert.IsNotNull(user, typeof(MembershipUser), "Membership user \"{0}\" not found", new object[] { base.UserName });
                    user.Comment = value;
                    Membership.UpdateUser(user);
                    this.comment = value;
                }
            }
        }

        public virtual string ContentLanguage
        {
            get
            {
                return (this.GetPropertyValueCore("ContentLanguage") as string);
            }
            set
            {
                this.SetPropertyValueCore("ContentLanguage", value);
            }
        }

        public virtual CultureInfo Culture
        {
            get
            {
                CultureInfo culture = this.GetCulture();
                if (culture.IsNeutralCulture)
                {
                    culture = CultureInfo.CreateSpecificCulture(culture.Name);
                }
                return culture;
            }
        }

        private Dictionary<string, string> CustomProperties
        {
            get
            {
                Dictionary<string, string> customProperties = this.customProperties;
                if (customProperties != null)
                {
                    return customProperties;
                }
                lock (this.propertiesLock)
                {
                    Func<string, string> elementSelector = null;
                    customProperties = this.SerializedData as Dictionary<string, string>;
                    if (customProperties != null)
                    {
                        this.customProperties = customProperties;
                        return customProperties;
                    }
                    NameValueCollection oldFormatData = this.SerializedData as NameValueCollection;
                    if (oldFormatData != null)
                    {
                        if (elementSelector == null)
                        {
                            elementSelector = key => oldFormatData[key];
                        }
                        customProperties = oldFormatData.AllKeys.ToDictionary<string, string, string>(key => key, elementSelector);
                    }
                    else
                    {
                        customProperties = new Dictionary<string, string>();
                    }
                    this.customProperties = customProperties;
                    if (!base.IsAnonymous)
                    {
                        this.SerializedData = customProperties;
                    }
                    return customProperties;
                }
            }
        }

        public virtual string Email
        {
            get
            {
                if (this.IsUserVirtual())
                {
                    return (this.GetPropertyValueCore("Email") as string);
                }
                MembershipUser membershipUser = this.MembershipUser;
                if (membershipUser == null)
                {
                    return null;
                }
                return membershipUser.Email;
            }
            set
            {
                if (this.IsUserVirtual())
                {
                    this.SetPropertyValueCore("Email", value);
                }
                else if (value != this.Email)
                {
                    this.email = value;
                    this.emailChanged = true;
                }
            }
        }

        public virtual string FullName
        {
            get
            {
                return (this.GetPropertyValueCore("FullName") as string);
            }
            set
            {
                this.SetPropertyValueCore("FullName", value);
            }
        }

        public virtual string Icon
        {
            get
            {
                return (this.GetPropertyValueCore("Icon") as string);
            }
            set
            {
                this.SetPropertyValueCore("Icon", value);
            }
        }

        public virtual bool IsAdministrator
        {
            get
            {
                object propertyValueCore = this.GetPropertyValueCore("IsAdministrator");
                try
                {
                    return (bool)propertyValueCore;
                }
                catch
                {
                    if (this.IsUserVirtual())
                    {
                        return ((this.ProfileUser != null) && this.ProfileUser.RuntimeSettings.IsAdministrator);
                    }
                    Log.SingleWarn("Cannot get IsAdministrator property value from the profile for user '{0}'".FormatWith(new object[] { base.UserName }), this);
                    return false;
                }
            }
            set
            {
                this.SetPropertyValueCore("IsAdministrator", value);
            }
        }

        public virtual string this[string propertyName]
        {
            get
            {
                Assert.ArgumentNotNull(propertyName, "propertyName");
                if (this.IsUserVirtual())
                {
                    User profileUser = this.ProfileUser;
                    if ((profileUser == null) || !profileUser.RuntimeSettings.Properties.ContainsKey(propertyName))
                    {
                        return string.Empty;
                    }
                    return ((profileUser.RuntimeSettings.Properties[propertyName] as string) ?? string.Empty);
                }
                if (!IsCoreProperty(propertyName))
                {
                    return this.GetCustomProperty(propertyName);
                }
                object propertyValueCore = this.GetPropertyValueCore(propertyName);
                if (propertyValueCore != null)
                {
                    return propertyValueCore.ToString();
                }
                return string.Empty;
            }
            set
            {
                Assert.ArgumentNotNull(propertyName, "propertyName");
                Assert.ArgumentNotNull(value, "value");
                if (this.IsUserVirtual())
                {
                    this.ProfileUser.RuntimeSettings.Properties[propertyName] = value;
                }
                else if (IsCoreProperty(propertyName) && !this.IsUserVirtual())
                {
                    this.SetPropertyValueCore(propertyName, value);
                }
                else
                {
                    this.SetCustomProperty(propertyName, value);
                }
            }
        }

        public virtual string LegacyPassword
        {
            get
            {
                return StringUtil.GetString(this.GetPropertyValueCore("LegacyPassword"));
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.SetPropertyValueCore("LegacyPassword", value);
            }
        }

        public virtual string ManagedDomainNames
        {
            get
            {
                return StringUtil.GetString(this.GetPropertyValueCore("ManagedDomainNames"));
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.SetPropertyValueCore("ManagedDomainNames", value);
            }
        }

        internal MembershipUser MembershipUser
        {
            get
            {
                if (this.innerMembershipUser == null)
                {
                    this.innerMembershipUser = new Reference<MembershipUser>(this.GetUser());
                }
                return this.innerMembershipUser.Value;
            }
        }

        public virtual string Name
        {
            get
            {
                return (this.GetPropertyValueCore("Name") as string);
            }
            set
            {
                this.SetPropertyValueCore("Name", value);
            }
        }

        public virtual string Portrait
        {
            get
            {
                return StringUtil.GetString(new string[] { this.GetPropertyValueCore("Portrait") as string, "people/16x16/user1.png" });
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.SetPropertyValueCore("Portrait", value);
            }
        }

        public virtual string ProfileItemId
        {
            get
            {
                string propertyValueCore = this.GetPropertyValueCore("ProfileItemId") as string;
                if (string.IsNullOrEmpty(propertyValueCore))
                {
                    Domain accountDomain = Domain.GetAccountDomain(base.UserName);
                    propertyValueCore = (accountDomain == null) ? string.Empty : accountDomain.DefaultProfileItemID;
                }
                return propertyValueCore;
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.SetPropertyValueCore("ProfileItemId", value);
            }
        }

        public virtual User ProfileUser
        {
            get
            {
                if ((this.profileUser == null) && (Membership.GetUser(base.UserName) != null))
                {
                    this.profileUser = User.FromName(base.UserName, false);
                }
                return this.profileUser;
            }
            set
            {
                this.profileUser = value;
            }
        }

        public virtual string RegionalIsoCode
        {
            get
            {
                return StringUtil.GetString(new string[] { this.GetPropertyValueCore("RegionalIsoCode") as string, Settings.DefaultRegionalIsoCode });
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.SetPropertyValueCore("RegionalIsoCode", value);
            }
        }

        [SettingsSerializeAs(SettingsSerializeAs.Binary)]
        public virtual object SerializedData
        {
            get
            {
                return this.GetPropertyValueCore("SerializedData");
            }
            set
            {
                this.SetPropertyValueCore("SerializedData", value);
            }
        }

        public virtual string StartUrl
        {
            get
            {
                return (this.GetPropertyValueCore("StartUrl") as string);
            }
            set
            {
                this.SetPropertyValueCore("StartUrl", value);
            }
        }

        public virtual string State
        {
            get
            {
                MembershipUser membershipUser = this.MembershipUser;
                if (membershipUser == null)
                {
                    return string.Empty;
                }
                string str = string.Empty;
                if (!membershipUser.IsApproved)
                {
                    if (!string.IsNullOrEmpty(str))
                    {
                        str = str + ", ";
                    }
                    str = str + Translate.Text("Disabled");
                }
                if (!membershipUser.IsLockedOut)
                {
                    return str;
                }
                if (!string.IsNullOrEmpty(str))
                {
                    str = str + ", ";
                }
                return (str + Translate.Text("Locked Out"));
            }
        }
    }


}
