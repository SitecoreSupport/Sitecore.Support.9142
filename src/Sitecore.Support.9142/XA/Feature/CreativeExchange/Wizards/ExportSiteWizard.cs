using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Sitecore.Buckets.Extensions;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Jobs;
using Sitecore.Jobs.AsyncUI;
using Sitecore.Web;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Pages;
using Sitecore.Web.UI.Sheer;
using Sitecore.XA.Feature.CreativeExchange.Enums.TypeSafe;
using Sitecore.XA.Feature.CreativeExchange.Extensions;
using Sitecore.XA.Feature.CreativeExchange.Jobs.Export;
using Sitecore.XA.Feature.CreativeExchange.Models.Export;
using Sitecore.XA.Feature.CreativeExchange.Models.Messages;
using Sitecore.XA.Feature.CreativeExchange.Pipelines.Export.GetExportContext;
using Sitecore.XA.Feature.CreativeExchange.Services;
using Sitecore.XA.Foundation.IoC;
using Sitecore.XA.Foundation.Multisite;
using Sitecore.XA.Foundation.SitecoreExtensions.Utils;
using CreativeExchangeFeature = Sitecore.XA.Feature.CreativeExchange;
using Message = Sitecore.Web.UI.Sheer.Message;

namespace Sitecore.Support.XA.Feature.CreativeExchange.Wizards
{
    public class ExportSiteWizard : WizardForm, IDisposable
    {
        protected JobMonitor Monitor;
        [UsedImplicitly]
        protected Literal ContextInfo;
        [UsedImplicitly]
        protected Border Devices;
        [UsedImplicitly]
        protected Border Languages;
        [UsedImplicitly]
        protected Border MarkupMode;
        [UsedImplicitly]
        protected Border ExportScope;
        [UsedImplicitly]
        protected Border Buckets;
        [UsedImplicitly]
        protected Border StorageType;
        [UsedImplicitly]
        protected Edit FileSizeLimit;
        [UsedImplicitly]
        protected Listview FolderList;
        [UsedImplicitly]
        protected Edit FolderName;
        [UsedImplicitly]
        protected Listview MessageList;
        [UsedImplicitly]
        protected Border AdditionaInfo;
        [UsedImplicitly]
        protected Border Success;
        [UsedImplicitly]
        protected Border Download;
        [UsedImplicitly]
        protected Border Error;
        [UsedImplicitly]
        protected Scrollbox SettingsPane;
        [UsedImplicitly]
        protected Memo ErrorMessage;

        [UsedImplicitly]
        protected Border Sites;

        public SiteInfo Site { get; set; }
        public virtual ICreativeExchangeStorageService CreativeExchangeStorageService { get; set; }

        protected string OutputPath
        {
            get
            {
                return StringUtil.GetString(Context.ClientPage.ServerProperties["OutputPath"]);
            }
            set
            {
                Context.ClientPage.ServerProperties["OutputPath"] = value;
            }
        }

        public Item Item { get; set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Item = Support.XA.Feature.CreativeExchange.UIUtil.GetItemFromQueryString(Client.ContentDatabase);
            if (Item != null)
            {
                Site = ServiceLocator.Current.Resolve<ISiteInfoResolver>().GetSiteInfo(Item);
                Item home = Client.ContentDatabase.GetItem(ServiceLocator.Current.Resolve<ISiteInfoResolver>().GetStartPath(Site), Item.Language) ?? Item;

                ContextInfo.Text = string.Format(Translate.Text(CreativeExchangeFeature.Texts.YouAreAboutToExportTheVersionOfTheSite), home.Language.GetDisplayName(), Site.Name);

                AddSitesRadio(Site, Item);
                AddDeviceSelectionButtons(home);
                AddLanguagesSelectionButtons(home);
                AddExportScopeRadio();
                AddBucketExportModeRadio();
                AddMarkupModeRadio();
                AddStorageTypeRadio(home);
                MessageList.OnSelectionChanged += MessageList_OnSelectionChanged;

                if (!Context.ClientPage.IsEvent)
                {
                    if (Monitor == null)
                    {
                        Monitor = new JobMonitor();
                        Monitor.ID = "Monitor";
                        Context.ClientPage.Controls.Add(Monitor);
                    }
                }
                else if (Monitor == null)
                {
                    Monitor = (JobMonitor)Context.ClientPage.FindControl("Monitor");
                }
            }
            else
            {
                //SXA-Website does not exists in web db.
                Error.Visible = true;
                Active = "LastPage";
                BackButton.Disabled = true;
                ErrorMessage.Value = "Item does not exist";
            }
        }


        protected override void ActivePageChanged(string page, string oldPage)
        {
            base.ActivePageChanged(page, oldPage);
            if (page == "Exporting")
            {
                NextButton.Disabled = true;
                BackButton.Disabled = true;
                CancelButton.Disabled = true;
                Context.ClientPage.SendMessage(this, "exportsite:export");
            }
            else if (page == "Results")
            {
                BackButton.Disabled = true;
            }
        }

        [HandleMessage("exportsite:export")]
        protected void OnExport(Message message)
        {
            var args = new GetExportContextArgs
            {
                ExportContext = new ExportContext(),
                HttpContextData = new HttpContextData(),
                Item = Item,
                Site = Site
            };
            var exportBackgroundJob = ServiceLocator.Current.Resolve<IExportBackgroundJob>();
            Monitor.Start("Export", "CreativeExchange", () => exportBackgroundJob.Run(args));
        }

        [HandleMessage("exportsite:fail")]
        protected void OnFail(Message message)
        {
            Job job = JobManager.GetJob(Monitor.JobHandle);
            Exception exception = job.Status.Result as Exception;
            if (exception == null)
            {
                return;
            }

            ErrorMessage.Value = exception.Message + '\n' + exception.StackTrace;
            Error.Visible = true;
            Active = "LastPage";
            BackButton.Disabled = true;
        }

        [HandleMessage("exportsite:success")]
        protected void OnSuccess(Message message)
        {
            Job job = JobManager.GetJob(Monitor.JobHandle);
            ExportJobResponse response = job.Status.Result as ExportJobResponse;
            if (response == null)
            {
                return;
            }

            OutputPath = response.Result?.Result ?? String.Empty;
            if (HttpContext.Current.Request["rbDestination"] == "Zip" && HttpContext.Current.Request["cbPurge"] == "1")
            {
                foreach (string file in Directory.GetFiles(Settings.PackagePath + @"\CreativeExchange").Where(f => f != OutputPath))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        //Do nothing
                    }
                }
            }

            HttpContext.Current.Session.Add(CreativeExchangeFeature.Constants.CreativeExchangeMessages, response.Messages);
            foreach (MessageBase entry in response.Messages)
            {
                ListviewItem item = entry.GetListviewItem();
                Context.ClientPage.AddControl(MessageList, item);
                item.ID = Control.GetUniqueID("I");
            }

            Success.Visible = true;
            Download.Visible = OutputPath.EndsWith(".zip");
            Active = "Results";

            if (response.Messages.Count == 0)
            {
                Next();
                BackButton.Disabled = false;
            }
            else
            {
                MessageList.RefreshListview();
            }
        }

        [HandleMessage("exportsite:download", true)]
        protected void OnDownloadPackage(ClientPipelineArgs args)
        {
            if (!OutputPath.IsNullOrEmpty())
            {
                SheerResponse.Download(OutputPath);
            }
        }

        private void MessageList_OnSelectionChanged(object sender, EventArgs e)
        {
            if (MessageList.SelectedItems.Length == 1)
            {
                ListviewItem selectedItem = MessageList.SelectedItems[0];
                int index = MessageList.Items.ToList().FindIndex(
                    p => p.ColumnValues["comment"] == selectedItem.ColumnValues["comment"]);
                List<MessageBase> messages = HttpContext.Current.Session[CreativeExchangeFeature.Constants.CreativeExchangeMessages] as List<MessageBase>;
                if (messages != null && messages.Count > index)
                {
                    MessageBase message = messages[index];
                    AdditionaInfo.InnerHtml = message.RenderAdditionalInfo();
                }
            }
        }

        protected virtual void AddDeviceSelectionButtons(Item home)
        {
            LayoutField layoutField = new LayoutField(home);
            var deviceItems = Client.ContentDatabase.Resources.Devices.GetAll().Where(deviceItem => layoutField.GetLayoutID(deviceItem) != ID.Null);
            Devices.AddRadioButtons(deviceItems, i => i.DisplayName, i => i.ID.ToString(), "rbDevice");
        }

        protected virtual void AddLanguagesSelectionButtons(Item home)
        {
            var db = home.Database;
            var languages = home.Languages.Where(l => db.GetItem(home.ID, l).Versions.Count > 0);
            Languages.AddRadioButtons(languages, i => i.Name, i => i.Name, "rbLanguage");
        }

        protected virtual void AddMarkupModeRadio()
        {
            MarkupMode.AddRadioButtons(CreativeExchangeFeature.Enums.TypeSafe.MarkupMode.GetAll(), i => i.DisplayName, i => i.Id.ToString(), "rbMarkupMode");
        }

        protected virtual void AddExportScopeRadio()
        {
            ExportScope.AddRadioButtons(CreativeExchangeFeature.Enums.TypeSafe.ExportScope.GetAll(), i => i.DisplayName, i => i.Id.ToString(), "rbExportScope");
        }

        protected virtual void AddBucketExportModeRadio()
        {
            Buckets.AddRadioButtons(BucketExportMode.GetAll(), i => i.DisplayName, i => i.Id.ToString(), "rbBucketExportMode");
        }

        protected virtual void AddStorageTypeRadio(Item home)
        {
            CreativeExchangeStorageService = ServiceLocator.Current.Resolve<ICreativeExchangeStorageService>();
            var creativeExchangeStorageDefinitions = CreativeExchangeStorageService.GetStorageServicesDefinitions(home);

            if (creativeExchangeStorageDefinitions == null)
            {
                ContextInfo.Text = CreativeExchangeFeature.Texts.CouldNotFindAnyStorageDefinitionInYourSiteExportCannotBePerformed;
                NextButton.Disabled = true;
                SettingsPane.Visible = false;
            }
            else
            {
                StorageType.AddRadioButtons(creativeExchangeStorageDefinitions, i => i.Name, i => i.Item.ID.ToString(), "rbStorageType");
            }
        }


        protected virtual void AddSitesRadio(SiteInfo site, Item item)
        {
            var siteInfos = ServiceLocator.Current.Resolve<ISiteInfoResolver>().Sites.Where(s => item.Paths.FullPath.StartsWith(s.RootPath + s.StartItem, StringComparison.InvariantCultureIgnoreCase));
            Sites.AddRadioButtons(siteInfos, i => i.Name, i => i.Name, "rbSite");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Monitor != null)
            {
                Monitor.Dispose();
            }
        }
    }
}
