using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sitecore.Modules.EmailCampaign.Core;
using Sitecore.Pipelines;
using Sitecore.Support;

namespace Sitecore.Support.Modules.EmailCampaign.Core
{
  public class CoreFactoryExtRecipientImport : CoreFactory
  {
    public CoreFactoryExtRecipientImport()
      : base()
    {
    }
    // Sitecore.Modules.EmailCampaign.Core.CoreFactory
    public override RecipientImporter GetRecipientImporter()
    {
      return new RecipientImporterExt();
    }


  }
  public class SwitchECMCoreFactory
  {
    public void Process(PipelineArgs args)
    {
      CoreFactory.Instance = new CoreFactoryExtRecipientImport();
    }
  }
}
