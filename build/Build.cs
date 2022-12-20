using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Pulumi;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "continuous",
    GitHubActionsImage.UbuntuLatest,
    On = new[] { GitHubActionsTrigger.Push },
    InvokedTargets = new[] { nameof(Publish) })]
class Build : NukeBuild
{
    AbsolutePath SourceDirectory => RootDirectory / "src";  
    AbsolutePath InfrastructureDirectory => RootDirectory / "infra";  
    AbsolutePath WebApiPackageDirectory => RootDirectory / "output";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath ArtifactFileName => ArtifactsDirectory / "api.zip";
    
    [Solution("./NukePulumi.sln")] readonly Solution Solution;

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("*/bin", "*/obj").ForEach(DeleteDirectory);  
            EnsureCleanDirectory(ArtifactsDirectory);  
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(_ => _  
                .SetProjectFile(Solution)  
                .SetConfiguration(Configuration)  
                .EnableNoRestore());  
        });

    Target Publish => _ => _  
        .DependsOn(Clean, Compile)
        .Produces(ArtifactFileName)
        .Executes(() =>  
        {  
            DotNetPublish(_ => _  
                .SetProject(Solution.GetProject("WebApi"))  
                .SetConfiguration(Configuration)  
                .EnableNoBuild()  
                .SetOutput(WebApiPackageDirectory));  
  
            ZipFile.CreateFromDirectory(WebApiPackageDirectory, ArtifactFileName);  
        });
    
    Target ProvisionInfra => _ => _  
        .Description("Provision the infrastructure on Azure")  
        .Executes(() =>  
        {  
            PulumiTasks.PulumiUp(_ => _  
                .SetCwd(InfrastructureDirectory)  
                .SetStack("dev")  
                .EnableSkipPreview());  
        });
    
    Target Deploy => _ => _  
        .DependsOn(Publish)  
        .After(ProvisionInfra)  
        .Executes(async () =>  
        {  
            var publishingUsername = GetPulumiOutput("publishingUsername");  
            var publishingUserPassword = GetPulumiOutput("publishingUserPassword");  
            var base64Auth = Convert.ToBase64String(Encoding.Default.GetBytes($"{publishingUsername}:{publishingUserPassword}"));  
  
            await using var package = File.OpenRead(ArtifactFileName);  
            using var httpClient = new HttpClient();  
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);  
            await httpClient.PostAsync($"https://{GetPulumiOutput("appServiceName")}.scm.azurewebsites.net/api/zipdeploy",  
                new StreamContent(package));  
        });
    
    string GetPulumiOutput(string outputName)  
    {  
        return PulumiTasks.PulumiStackOutput(_ => _  
                .SetCwd(InfrastructureDirectory)  
                .SetPropertyName(outputName)  
                .EnableShowSecrets()
                .DisableProcessLogOutput())  
            .StdToText();  
    }
}
