﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sitecore.Diagnostics;
using Sitecore.Modules.EmailCampaign.Core;
using Sitecore.Support.Modules.EmailCampaign.Core;
using Sitecore.Modules.EmailCampaign;
using System.Web.Security;
using Sitecore.Security.Accounts;
using Sitecore.Modules.EmailCampaign.Messages;
using Sitecore.IO;
using System.IO;
using Sitecore.Data.Items;
using Sitecore.Text;

namespace Sitecore.Support.Modules.EmailCampaign.Core
{
  class RecipientImporterExt : RecipientImporter
  {
    public int CsvRowsProcessed { get; set; }

    public RecipientImporterExt()
      : base()
    {
    }
    // Sitecore.Modules.EmailCampaign.Core.RecipientImporter
    private void AddUserToRoles(ImportOptions options, string userName)
    {
      string[] assignedRoles = Roles.GetRolesForUser(userName);
      List<string> list = (
          from role in options.Roles
          where !assignedRoles.Any(r => r == role)
          select role).ToList<string>();
      if (list.Count > 0)
      {
        Roles.AddUserToRoles(userName, list.ToArray());
      }
    }

    // Sitecore.Modules.EmailCampaign.Core.RecipientImporter
    private int GetEmailColumnIndex(ImportOptions options, List<string> headers)
    {
      int result = -1;
      for (int i = 0; i < headers.Count; i++)
      {
        if (options.MappedProperties["Email"].Equals(headers[i]))
        {
          result = i;
          options.MappedProperties.Remove("Email");
          break;
        }
      }
      return result;
    }

    // Sitecore.Modules.EmailCampaign.Core.RecipientImporter
    public override string PerformImport(ImportOptions options)
    {
      Assert.ArgumentNotNull(options, "options");
      int num = 0;
      int num2 = 0;
      int num3 = 0;
      int num4 = 0;
      string result;

      string emailReport = Sitecore.Configuration.Settings.GetSetting("RecipientImporterExt.PathToEmailReport", "/temp")
        + "/ProblemRecordsFor_" + Path.GetFileNameWithoutExtension(options.Filename)
        + "_" + DateTime.Now.ToString("s").Replace(":", string.Empty) + ".csv";

      string pathToEmailReport = FileUtil.MapPath(emailReport);
      StreamWriter streamWriter = new StreamWriter(pathToEmailReport);

      using (CsvFile csvFile = new CsvFile(options.Filename))
      {
        try
        {
          List<string> list = csvFile.ReadLine();
          streamWriter.WriteLine(string.Join(",", list.ToArray()));
          int emailColumnIndex = this.GetEmailColumnIndex(options, list);
          if (emailColumnIndex < 0)
          {
            result = string.Empty;
            return result;
          }
          List<string> list2 = csvFile.ReadLine();
          while (list2 != null)
          {
            CsvRowsProcessed++;

            try
            {
              bool flag = false;
              if (list.Count < list2.Count)
              {
                num4++;
                Log.Info(string.Format("RecipientImporterExt debug info: Invalid row {0}", string.Join(",", list2.ToArray())), this);
                streamWriter.WriteLine(string.Join(",", list2.ToArray()));
                list2 = csvFile.ReadLine();
              }
              else
              {
                string text = list2[emailColumnIndex];
                Log.Info(string.Format("RecipientImporterExt debug info: Processing the {0} record ...", text), this);
                if (!Util.IsValidEmail(text))
                {
                  text = this.TryFindEmail(text);
                  if (string.IsNullOrEmpty(text))
                  {
                    num3++;
                    Log.Info(string.Format("RecipientImporterExt debug info:  Invalid email {0}", text), this);
                    streamWriter.WriteLine(string.Join(",", list2.ToArray()));
                    list2 = csvFile.ReadLine();
                    continue;
                  }
                }
                string text2 = options.DomainName + "\\" + Util.AddressToUserName(text);
                Contact contactFromName;
                if (User.Exists(text2))
                {
                  if (options.ConflictOption == ImportOptions.ConflictOptions.SkipUser)
                  {
                    this.AddUserToRoles(options, text2);
                    num2++;
                    Log.Info(string.Format("RecipientImporterExt debug info:  Record is skipped due to SkipUser setting ...", text), this);
                    streamWriter.WriteLine(string.Join(",", list2.ToArray()));
                    list2 = csvFile.ReadLine();
                    continue;
                  }
                  flag = (options.ConflictOption == ImportOptions.ConflictOptions.KeepProperties);
                  contactFromName = Factory.GetContactFromName(text2);
                }
                else
                {
                  MembershipUser membershipUser = Membership.CreateUser(text2, text2);
                  membershipUser.ResetPassword();
                  contactFromName = Factory.GetContactFromName(text2);
                  contactFromName.Profile.ProfileItemId = options.Root.Settings.SubscriberProfile;
                  contactFromName.Profile.Save();
                }
                if (!flag)
                {
                  contactFromName.Profile.Email = text;
                  contactFromName.Profile["IsAnonymousSubscriber"] = "true";
                }
                foreach (string current in options.MappedProperties.Keys)
                {
                  for (int i = 0; i < list.Count; i++)
                  {
                    if (options.MappedProperties[current].Equals(list[i]))
                    {
                      if (!flag || string.IsNullOrEmpty(contactFromName.Profile[current]))
                      {
                        contactFromName.Profile[current] = list2[i];
                      }
                      break;
                    }
                  }
                }
                contactFromName.Profile.Save();
                this.AddUserToRoles(options, text2);
                num++;
                list2 = csvFile.ReadLine();
              }


            }
            catch (Exception e)
            {
              Log.Info(string.Format("RecipientImporterExt debug info:  Invalid email {0}", list2.ToString()), this);
              streamWriter.WriteLine(string.Join(",", list2.ToArray()));
              list2 = csvFile.ReadLine();
              Logging.LogError(e);
            }
          }
        }
        catch (Exception e)
        {
          Logging.LogError(e);
        }
        finally
        {
          streamWriter.Close();
        }
      }
      result = string.Concat(new object[]
	    {
		    num,  "|",
		    num2, "|",
		    num3, "|",
		    num4
	    });

      try
      {
        Sitecore.Data.Database master = Sitecore.Configuration.Factory.GetDatabase("master");
        Sitecore.Data.Items.Item itemToSend = master.GetItem(Sitecore.Configuration.Settings.GetSetting("RecipientImporterExt.MessagePath", "/sitecore/content/Home/Email Campaign/Messages/Service Messages/Self-Service Subscription/Subscription Notification"));
        MessageItem messageItem = Sitecore.Modules.EmailCampaign.Factory.GetMessage(itemToSend);
        //var report = new MediaItem();
        //messageItem.Attachments.Add(
        messageItem.Body = string.Format("Recipients imported: {0} <br/> E-mail addresses already exist: {1} <br/> E-mail addresses not provided: {2} <br/> Users not imported as required fields not available: {3}"
                                        + " <br/> You can check report file with the failed records here: {4}. <br/> Import finished at " + DateTime.Now.ToString(@"d/M/yyyy hh:mm:ss tt"), num, num2, num3, num4, pathToEmailReport);
        messageItem.Subject = "User import from "+Path.GetFileNameWithoutExtension(options.Filename)+" finished." ;
        SendingManager sendingManager = new SendingManager(messageItem);

        ListString usernamesToSend = new ListString(Sitecore.Configuration.Settings.GetSetting("RecipientImporterExt.SendTo", "sitecore\\admin"), ',');
        if (usernamesToSend.Count == 0)
        {
            Log.Info("RecipientImporterExt debug info: no users to send email to ", this);
            return result;
        }
        foreach (string nameString in usernamesToSend)
        {
            Contact contactToSend = Sitecore.Modules.EmailCampaign.Factory.GetContactFromName(nameString);
            if (contactToSend != null)
            {
                Log.Info("RecipientImporterExt debug info: Sending notification about the import to " + contactToSend.Profile.Email, this);
                sendingManager.SendStandardMessage(contactToSend);
            }
        }

      
        //User user = Sitecore.Context.User;
        //if (user != null)
        //{
        //    string username = user.Name;
        //    Log.Info("------------contactToSend2: " + username, this);
        //    sendingManager.SendStandardMessage(Sitecore.Modules.EmailCampaign.Factory.GetContactFromName(username));
        //}
      }
      catch (Exception e)
      {

        Logging.LogError(e);
      }


      return result;
    }

    // Sitecore.Modules.EmailCampaign.Core.RecipientImporter
    private string TryFindEmail(string text)
    {
      string result;
      if (string.IsNullOrEmpty(text))
      {
        result = string.Empty;
      }
      else
      {
        int num = text.IndexOf('@');
        if (num < 1 || num > text.Length - 2)
        {
          result = string.Empty;
        }
        else
        {
          int num2 = 0;
          int num3 = text.Length - 1;
          for (int i = num - 1; i >= 0; i--)
          {
            char c = text[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
              num2 = i + 1;
              break;
            }
          }
          for (int i = num + 1; i < text.Length; i++)
          {
            char c = text[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
              num3 = i - 1;
              break;
            }
          }
          string text2 = text.Substring(num2, num3 - num2 + 1);
          result = (Util.IsValidEmail(text2) ? text2 : string.Empty);
        }
      }
      return result;
    }

  }
}

namespace Sitecore.Support.Modules.EmailCampaign.Core
{
  using System;
  using System.Collections.Generic;
  using System.IO;

  using Sitecore.Diagnostics;
  using Sitecore.IO;
  using Sitecore.Modules.EmailCampaign.Exceptions;
  using Sitecore.StringExtensions;

  internal class CsvFile : IDisposable
  {
    private StreamReader reader;

    /// <exception cref="EmailCampaignException"><c>EmailCampaignException</c>.</exception>
    public CsvFile(string filename)
    {
      Assert.ArgumentNotNull(filename, "filename");

      if (!"csv".Equals(FileUtil.GetExtension(filename), StringComparison.OrdinalIgnoreCase))
      {
        throw new EmailCampaignException("'{0}' is not a CSV file!".FormatWith(FileUtil.GetFileName(filename)));
      }

      this.reader = new StreamReader(filename);
      Util.AssertNotNull(this.reader);
    }

    ~CsvFile()
    {
      this.Dispose();
    }

    public void Dispose()
    {
      if (this.reader != null)
      {
        this.reader.Close();
      }
    }

    public List<string> ReadLine()
    {
      List<string> list = new List<string>();

      if (this.reader == null || this.reader.EndOfStream)
      {
        return null;
      }

      this.ReadToList(list, string.Empty, 0);

      return list;
    }

    private void ReadToList(List<string> list, string rest, int quote)
    {
      if (this.reader == null)
      {
        return;
      }

      int start = 0;
      string line = this.reader.ReadLine();

      if (!string.IsNullOrEmpty(line))
      {
        for (int i = 0; i < line.Length; i++)
        {
          switch (line[i])
          {
            case '"':
              quote = (quote == 2) ? 1 : quote + 1;

              break;
            case ',':
            case ';':
              if (quote == 0 || quote == 2)
              {
                int end = i - 1;

                if (string.IsNullOrEmpty(rest))
                {
                  if (quote == 2)
                  {
                    start++;
                    end--;
                  }

                  list.Add(line.Substring(start, end - start + 1).Replace("\"\"", "\""));
                }
                else
                {
                  string buf = rest + line.Substring(start, end - start + 1);

                  if (quote == 2)
                  {
                    buf = buf.Substring(1, buf.Length - 2);
                  }

                  list.Add(buf.Replace("\"\"", "\""));
                  rest = string.Empty;
                }

                quote = 0;
                start = i + 1;
              }

              break;
          }
        }
      }

      if (quote != 1)
      {
        int end = line.Length - 1;

        if (quote == 2)
        {
          start++;
          end--;
        }

        list.Add(line.Substring(start, end - start + 1).Replace("\"\"", "\""));
      }
      else
      {
        this.ReadToList(list, rest + line.Substring(start, line.Length - start) + "\r\n", quote);
      }
    }
  }
}