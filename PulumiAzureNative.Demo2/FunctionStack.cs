using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Deployment = Pulumi.Deployment;

namespace PulumiAzureNative.Demo2
{
    internal class FunctionStack : Stack
    {
        #region Outputs
        
        [Output] public Output<string> TestEndpoint { get; set; }
        [Output] public Output<string> TestEndpoint1 { get; set; }
        
        #endregion
        
        public FunctionStack()
        {
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

            #endregion
            
            #region Plan
            
            var planName = $"{projectName}-{stackName}-plan";
            var plan = new AppServicePlan(planName, new AppServicePlanArgs
            {
                ResourceGroupName = resourceGroup.Name,
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
            
            #region Func Blob
            
            var container = new BlobContainer("zips", new BlobContainerArgs
            {
                AccountName = storageAccount.Name,
                ResourceGroupName = resourceGroup.Name,
                PublicAccess = PublicAccess.None
            });
            
            var blob = new Blob("funczip", new BlobArgs
            {
                AccountName = storageAccount.Name,
                ResourceGroupName = resourceGroup.Name,
                ContainerName = container.Name,
                Type = BlobType.Block,
                Source = new FileArchive("../PulumiAzureNative.Func/bin/Debug/netcoreapp3.1/publish")
            });
            
            var codeBlobUrl = SignedBlobReadUrl(blob, container, storageAccount, resourceGroup);
            
            #endregion 

            #region Func
            
            var funcName = $"{projectName}-{stackName}-func";
            var func = new WebApp(funcName, new WebAppArgs
            {
                Name = funcName,
                Kind = "FunctionApp",
                ResourceGroupName = resourceGroup.Name,
                ServerFarmId = plan.Id,
                SiteConfig = new SiteConfigArgs
                {
                    AppSettings = new[]
                    {
                        new NameValuePairArgs{
                            Name = "AzureWebJobsStorage",
                            Value = GetStorageConnectionString(resourceGroup.Name, storageAccount.Name)
                        },
                        new NameValuePairArgs{
                            Name = "runtime",
                            Value = "dotnet"
                        },
                        new NameValuePairArgs{
                            Name = "WEBSITE_RUN_FROM_PACKAGE",
                            Value = codeBlobUrl
                        }
                    }
                }
            });
            
            #endregion
            
            #region Func Abstraction
            /*
            var funcName1 = $"{projectName}-{stackName}-func1";
            var func1 = new PackageFunctionApp(funcName1, new PackageFunctionAppArgs
            {
                ProjectName = projectName,
                ResourceGroup = resourceGroup,
                StorageAccount = storageAccount,
                Plan = plan,
                Archive = new FileArchive("../PulumiAzureNative.Func/bin/Debug/netcoreapp3.1/publish")
            });
            */
            #endregion 

            TestEndpoint = Output.Format($"https://{func.DefaultHostName}/api/Hello?name=GlobalAzureTorino");
            //TestEndpoint1 = Output.Format($"https://{func1.FunctionApp.Apply(f => f.DefaultHostName)}/api/Hello?name=GlobalAzureTorino");
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
        static Output<string> SignedBlobReadUrl(Blob blob, BlobContainer container, StorageAccount account, ResourceGroup resourceGroup)
        {
            return Output.Tuple(blob.Name, container.Name, account.Name, resourceGroup.Name).Apply(t =>
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
                    ContentEncoding = "deflate",
                });
                return Output.Format($"https://{accountName}.blob.core.windows.net/{containerName}/{blobName}?{blobSAS.Result.ServiceSasToken}");
            });
        }
        
        #endregion
    }
}
