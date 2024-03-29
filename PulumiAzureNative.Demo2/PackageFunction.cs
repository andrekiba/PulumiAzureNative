using System;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Deployment = Pulumi.Deployment;

namespace PulumiAzureNative.Demo2
{
    public class PackageFunctionApp : ComponentResource
    {
        #region Fields
        
        readonly string stackName = Deployment.Instance.StackName;
        readonly Random random = new Random();
        
        #endregion
        
        #region Outputs
        
        [Output] public Output<WebApp> FunctionApp { get; set; }
        
        #endregion 
        
        public PackageFunctionApp(string name, PackageFunctionAppArgs args, ComponentResourceOptions? options = null) : 
            base("PulumiAzureNative.Demo2.PackageFunctionApp", name, options)
        {
            var resourceGroupName = args.ResourceGroup.Apply(rg => rg.Name);
            
            #region Storage Account
            
            var storageAccountName = $"{args.ProjectName}{stackName}st{random.Next(1,1000)}";
            Input<StorageAccount> storageAccount = args.StorageAccount ?? new StorageAccount(storageAccountName, new StorageAccountArgs
            {
                ResourceGroupName = resourceGroupName,
                AccountName = storageAccountName,
                Sku = new SkuArgs
                {
                    Name = SkuName.Standard_LRS
                },
                Kind = Kind.StorageV2
            });

            #endregion
            
            #region Func Blob
            
            var container = new BlobContainer($"zips{random.Next(1,1000)}", new BlobContainerArgs
            {
                AccountName = storageAccount.Apply(st => st.Name),
                ResourceGroupName = resourceGroupName,
                PublicAccess = PublicAccess.None
            });
            
            var blob = new Blob($"funczip{random.Next(1,1000)}", new BlobArgs
            {
                AccountName = storageAccount.Apply(st => st.Name),
                ResourceGroupName = resourceGroupName,
                ContainerName = container.Name,
                Type = BlobType.Block,
                Source = args.Archive
            });
            
            var codeBlobUrl = SignedBlobReadUrl(blob, container, storageAccount, args.ResourceGroup);
            
            #endregion
            
            #region Plan
            
            var planName = $"{args.ProjectName}-{stackName}-plan{random.Next(1,1000)}";
            var plan = args.Plan ?? new AppServicePlan(planName, new AppServicePlanArgs
            {
                ResourceGroupName = resourceGroupName,
                Name = planName,
                Kind = "Linux",
                // Consumption plan SKU
                Sku = new SkuDescriptionArgs
                {
                    Tier = "Dynamic",
                    Name = "Y1"
                },
                // For Linux, you need to change the plan to have Reserved = true property.
                Reserved = true
            });
            
            #endregion

            #region Func
            
            args.AppSettings.Add(new NameValuePairArgs{
                Name = "AzureWebJobsStorage",
                Value = GetStorageConnectionString(resourceGroupName, storageAccount.Apply(st => st.Name))
            });
            args.AppSettings.Add(new NameValuePairArgs{
                Name = "runtime",
                Value = "dotnet"
            });
            args.AppSettings.Add(new NameValuePairArgs{
                Name = "FUNCTIONS_EXTENSION_VERSION",
                Value = "~3"
            });
            args.AppSettings.Add(new NameValuePairArgs{
                Name = "WEBSITE_RUN_FROM_PACKAGE",
                Value = codeBlobUrl
            });
            
            var func = new WebApp(name, new WebAppArgs
            {
                Name = name,
                Kind = "FunctionApp",
                ResourceGroupName = resourceGroupName,
                ServerFarmId = plan.Apply(p => p.Id),
                SiteConfig = new SiteConfigArgs
                {
                    AppSettings = args.AppSettings
                }
            });

            FunctionApp = Output.Create(func);

            #endregion
        }
        
        #region Methods
        
        static Output<string> GetStorageConnectionString(Input<string> resourceGroupName, Input<string> accountName)
        {
            // Retrieve the primary storage account key.
            var storageAccountKeys = Output.All(resourceGroupName, accountName)
                .Apply(t => ListStorageAccountKeys.InvokeAsync(
                new ListStorageAccountKeysArgs
                {
                    ResourceGroupName = t[0],
                    AccountName = t[1]
                }));
            
            return storageAccountKeys.Apply(keys =>
            {
                var primaryStorageKey = keys.Keys[0].Value;

                // Build the connection string to the storage account.
                return Output.Format($"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={primaryStorageKey}");
            });
        }
        static Output<string> SignedBlobReadUrl(Blob blob, BlobContainer container, Input<StorageAccount> account, Input<ResourceGroup> resourceGroup)
        {
            return Output.Tuple(blob.Name, container.Name, account.Apply(a => a.Name), resourceGroup.Apply(rg => rg.Name))
                .Apply(t =>
            {
                (string blobName, string containerName, string accountName, string resourceGroupName) = t;

                var blobSAS = ListStorageAccountServiceSAS.InvokeAsync(new ListStorageAccountServiceSASArgs
                {
                    AccountName = accountName,
                    Protocols = HttpProtocol.Https,
                    SharedAccessStartTime = "2021-04-01",
                    SharedAccessExpiryTime = "2030-01-01",
                    Resource = SignedResource.C,
                    ResourceGroupName = resourceGroupName,
                    Permissions = Permissions.R,
                    CanonicalizedResource = "/blob/" + accountName + "/" + containerName,
                    ContentType = "application/json",
                    CacheControl = "max-age=5",
                    ContentDisposition = "inline",
                    ContentEncoding = "deflate"
                });
                return Output.Format($"https://{accountName}.blob.core.windows.net/{containerName}/{blobName}?{blobSAS.Result.ServiceSasToken}");
            });
        }
        
        #endregion 
    }
    
    public record PackageFunctionAppArgs
    {
        public string ProjectName { get; init; }
        public Input<ResourceGroup> ResourceGroup { get; init; }
        public Input<StorageAccount>? StorageAccount { get; init; }
        public Input<AppServicePlan>? Plan { get; init; }
        public Input<AssetOrArchive> Archive { get; init; }
        public InputList<NameValuePairArgs> AppSettings { get; set; } = new();
    }
}