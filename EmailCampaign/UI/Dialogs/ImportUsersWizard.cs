using Sitecore.Diagnostics;
using Sitecore.IO;
using Sitecore.Jobs;
using Sitecore.Modules.EmailCampaign;
using Sitecore.Modules.EmailCampaign.Core;
using Sitecore.Modules.EmailCampaign.UI.Dialogs;
using Sitecore.Support.Modules.EmailCampaign.Core;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Sheer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sitecore.Support.Modules.EmailCampaign.UI.Dialogs
{
  public class ImportUsersWizardExt : ImportUsersWizard
  {
    private readonly ITargetAudienceRepository targetAudienceRepository = Factory.Instance.GetTargetAudienceRepository();

    public override void CheckImport()
    {
      Job currentJob = JobHelper.CurrentJob;
      if (currentJob == null)
      {
        base.Next();
      }
      else if (!currentJob.IsDone)
      {
        if (currentJob.Status.Job.Options.CustomData is RecipientImporterExt)
        {
          var status = (currentJob.Status.Job.Options.CustomData as RecipientImporterExt);
          var statusString = string.Format("Importing...<br/>CSV rows processed: {0}", status.CsvRowsProcessed);
          SheerResponse.SetInnerHtml("StatusLiteral", statusString);
        }
        else
        {
          SheerResponse.SetInnerHtml("StatusLiteral", "Importing...");
        }
        SheerResponse.Timer("CheckImport", 300);
      }
      else
      {
        if (currentJob.Status.Result != null)
        {
          this.UpdateForm(currentJob.Status.Result.ToString());
        }
        base.Next();
      }
    }

    protected override bool TargetAudiencesCompleted()
    {
      ImportOptions options = new ImportOptions
      {
        Filename = FileUtil.MapPath("/temp/" + FileUtil.GetFileName(this.Filename.Value)),
        MappedProperties = this.MappedProperties,
        Root = this.Root,
        DomainName = this.DomainInput.Value
      };
      string str = Context.ClientPage.ClientRequest.Form[this.SkipUser.Name];
      if (string.IsNullOrEmpty(str) || str.Equals(this.SkipUser.Value))
      {
        options.ConflictOption = ImportOptions.ConflictOptions.SkipUser;
      }
      else
      {
        options.ConflictOption = str.Equals(this.OverwriteProperties.Value) ? ImportOptions.ConflictOptions.OverwriteProperties : ImportOptions.ConflictOptions.KeepProperties;
      }

      List<string> roles = new List<string>();
      try
      {
        this.AddOptInRoles(roles);
        this.AddOptOutRoles(roles);
        this.AddAdvancedRoles(roles);
      }
      catch (Exception exception)
      {
        SheerResponse.Alert(exception.Message, new string[0]);
        return false;
      }

      options.Roles = roles.ToArray();
      StartImportJob("Import Users", "PerformImport", CoreFactory.Instance.GetRecipientImporter(), new object[] { options });
      this.CheckImport();
      return true;
    }

    private static void StartImportJob(string name, string method, object helper, params object[] args)
    {
      string str = (Context.Site != null) ? Context.Site.Name : Util.GetContentSite().Name;
      JobOptions options = new JobOptions(name, method, str, helper, method, args);
      options.EnableSecurity = false;
      options.AfterLife = (TimeSpan.FromSeconds(3.0));
      options.WriteToLog = true;
      JobHelper.CurrentJob = JobManager.Start(options);

      if (helper is RecipientImporterExt)
      {
        JobHelper.CurrentJob.Options.CustomData = helper;
      }
    }

    private void AddAdvancedRoles(List<string> roles)
    {
      if (this.AdvancedOptions.Checked)
      {
        foreach (string str in this.RoleList.Value.Split(new char[] { '|' }))
        {
          if (!((str.Length <= 0) || roles.Contains(str)))
          {
            roles.Add(str);
          }
        }
      }
    }

    private void AddOptInRoles(List<string> roles)
    {
      foreach (ListviewItem item in this.SubscribeTo.Items)
      {
        TargetAudience targetAudience = this.targetAudienceRepository.GetTargetAudience(item.Value);
        if (targetAudience != null)
        {
          string domain = this.DomainCombobox.Value;
          string str2 = targetAudience.Storage.CreateRoleToExtraOptIn(domain);
          if (!string.IsNullOrEmpty(str2))
          {
            roles.Add(str2);
          }
        }
      }
    }

    private void AddOptOutRoles(List<string> roles)
    {
      foreach (ListviewItem item in this.OptOutOf.Items)
      {
        TargetAudience targetAudience = this.targetAudienceRepository.GetTargetAudience(item.Value);
        if (targetAudience != null)
        {
          string domain = this.DomainCombobox.Value;
          string str2 = targetAudience.Storage.CreateRoleToExtraOptOut(domain);
          if (!string.IsNullOrEmpty(str2))
          {
            roles.Add(str2);
          }
        }
      }
    }

    private void UpdateForm(string results)
    {
      if (!string.IsNullOrEmpty(results))
      {
        string[] strArray = results.Split(new char[] { '|' });
        if (strArray.Length >= 4)
        {
          this.NumImported.Text = (strArray[0]);
          this.NumEmailExists.Text = (strArray[1]);
          this.NumNoEmail.Text = (strArray[2]);
          this.NumBroken.Text = (strArray[3]);
          SheerResponse.Refresh(this.Results);
        }
      }
    }
  }
}
