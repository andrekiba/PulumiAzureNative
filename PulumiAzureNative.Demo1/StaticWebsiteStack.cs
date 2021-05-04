using System.IO;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Deployment = Pulumi.Deployment;
using Cdn = Pulumi.AzureNative.Cdn;

namespace PulumiAzureNative.Demo1
{
    internal class StaticWebsiteStack : Stack
    {
        public StaticWebsiteStack()
        {
            //var projectName = Deployment.Instance.ProjectName;
            const string projectName = "pulumiazurenative";
            var stackName = Deployment.Instance.StackName;
            
            #region Resource Group
            
            var resourceGroupName = $"{projectName}-{stackName}-rg";
            var resourceGroup = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName
            });
            
            #endregion
            
            #region Storage Account
            
            var storageAccountName = $"{projectName}{stackName}st";
            var storageAccount = new StorageAccount(storageAccountName, new StorageAccountArgs
            {
                ResourceGroupName = resourceGroup.Name,
                AccountName = storageAccountName,
                Sku = new SkuArgs
                {
                    Name = SkuName.Standard_LRS
                },
                Kind = Kind.StorageV2
            });
            
            var staticWebsiteName = $"{projectName}-{stackName}-sbs";
            var staticWebsite = new StorageAccountStaticWebsite(staticWebsiteName, new StorageAccountStaticWebsiteArgs
            {
                AccountName = storageAccount.Name,
                ResourceGroupName = resourceGroup.Name,
                IndexDocument = "index.html",
                Error404Document = "404.html"
            });
            
            #endregion
            
            #region Blobs
            
            var files = Directory.GetFiles("./wwwroot");
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var blob = new Blob(name, new BlobArgs
                {
                    ResourceGroupName = resourceGroup.Name,
                    AccountName = storageAccount.Name,
                    ContainerName = staticWebsite.ContainerName,
                    ContentType = "text/html",
                    Source = new FileAsset(file)
                });
            }

            #endregion
            
            #region CDN
            /*
            var cdnProfileName = $"{projectName}-{stackName}-cdn-prof";
            var cdnProfile = new Cdn.Profile(cdnProfileName, new Cdn.ProfileArgs
            {
                ProfileName = cdnProfileName,
                ResourceGroupName = resourceGroup.Name,
                Location = "global",
                Sku = new Cdn.Inputs.SkuArgs
                {
                    Name = Cdn.SkuName.Standard_Microsoft
                }
            });
            
            var storageEndpoint = storageAccount.PrimaryEndpoints.Apply(pe => pe.Web.Replace("https://", "").Replace("/", ""));

            var cdnEndpointName = $"{projectName}-{stackName}-cdn-end";
            var cdnEndpoint = new Cdn.Endpoint(cdnEndpointName, new Cdn.EndpointArgs
            {
                EndpointName = cdnEndpointName,
                IsHttpAllowed = false,
                IsHttpsAllowed = true,
                OriginHostHeader = storageEndpoint,
                Origins =
                {
                    new Cdn.Inputs.DeepCreatedOriginArgs
                    {
                        HostName = storageEndpoint,
                        HttpsPort = 443,
                        Name = "origin-storage-account"
                    }
                },
                ProfileName = cdnProfile.Name,
                QueryStringCachingBehavior = Cdn.QueryStringCachingBehavior.NotSet,
                ResourceGroupName = resourceGroup.Name
            });
            */
            #endregion 
            
            #region Output
            
            StaticEndpoint = storageAccount.PrimaryEndpoints.Apply(primaryEndpoints => primaryEndpoints.Web);
            PrimaryStorageKey = Output.Tuple(resourceGroup.Name, storageAccount.Name).Apply(names =>
                Output.CreateSecret(GetStorageAccountPrimaryKey(names.Item1, names.Item2)));
            //CdnEndpoint = cdnEndpoint.HostName.Apply(hostName => $"https://{hostName}");
            
            #endregion
        }
        
        #region Output
        
        [Output("primaryStorageKey")]
        public Output<string> PrimaryStorageKey { get; set; }
        
        [Output("staticEndpoint")]
        public Output<string> StaticEndpoint { get; set; }
        
        [Output("cdnEndpoint")]
        public Output<string> CdnEndpoint { get; set; }
        
        #endregion
        
        #region Methods
        
        static async Task<string> GetStorageAccountPrimaryKey(string resourceGroupName, string accountName)
        {
            var accountKeys = await ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs
            {
                ResourceGroupName = resourceGroupName,
                AccountName = accountName
            });
            return accountKeys.Keys[0].Value;
        }
        
        #endregion
    }
}
