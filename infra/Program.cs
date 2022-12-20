using Pulumi.AzureNative.Resources;
using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Deployment = Pulumi.Deployment;

return await Pulumi.Deployment.RunAsync(() =>
{
    var resourceGroup = new ResourceGroup($"rg-{Deployment.Instance.ProjectName}-{Deployment.Instance.StackName}");  
  
    var appServicePlan = new AppServicePlan($"plan-{Deployment.Instance.ProjectName}-{Deployment.Instance.StackName}", new AppServicePlanArgs  
    {  
        ResourceGroupName = resourceGroup.Name,  
        Kind = "App",  
        Sku = new SkuDescriptionArgs  
        {  
            Tier = "Basic",  
            Name = "B1",  
        },  
    });  
  
    var appService = new WebApp($"app-{Deployment.Instance.ProjectName}-{Deployment.Instance.StackName}", new WebAppArgs   
    {   
        ResourceGroupName = resourceGroup.Name,  
        ServerFarmId = appServicePlan.Id  
    });
    
    var publishingCredentials = ListWebAppPublishingCredentials.Invoke(new()  
    {  
        ResourceGroupName = resourceGroup.Name,  
        Name = appService.Name  
    });

    return new Dictionary<string, object?>  
    {  
        ["publishingUsername"] = Output.CreateSecret(publishingCredentials.Apply(c => c.PublishingUserName)),  
        ["publishingUserPassword"] = Output.CreateSecret(publishingCredentials.Apply(c => c.PublishingPassword)),  
        ["appServiceName"] = appService.Name  
    };
});