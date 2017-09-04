using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Jobs;
using Sitecore.Jobs.AsyncUI;
using Sitecore.Shell.Applications.Install.Dialogs;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Pages;
using Sitecore.Web.UI.Sheer;
using Sitecore.XA.Feature.CreativeExchange.Enums;
using Sitecore.XA.Feature.CreativeExchange.Extensions;
using Sitecore.XA.Feature.CreativeExchange.Jobs.Import;
using Sitecore.XA.Feature.CreativeExchange.Models.Import;
using Sitecore.XA.Feature.CreativeExchange.Models.Messages;
using Sitecore.XA.Feature.CreativeExchange.Pipelines.Import.GetImportContext;
using Sitecore.XA.Feature.CreativeExchange.Services;
using Sitecore.XA.Feature.CreativeExchange.Storage;
using Sitecore.XA.Foundation.IoC;
using Sitecore.XA.Foundation.Multisite;
using Sitecore.XA.Foundation.SitecoreExtensions.Utils;
using CreativeExchangeFeature = Sitecore.XA.Feature.CreativeExchange;
using Message = Sitecore.Web.UI.Sheer.Message;
using Sitecore.Diagnostics;

namespace Sitecore.Support.XA.Feature.CreativeExchange.Wizards
{
    public class ImportSiteWizard : WizardForm, IDisposable
    {
        protected JobMonitor Monitor;
        [UsedImplicitly]
        protected Literal ContextInfo;
        [UsedImplicitly]
        protected Listview PackageList;
        [UsedImplicitly]
        protected Edit PackageName;
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
        protected Border Error;
        [UsedImplicitly]
        protected Memo ErrorMessage;
        [UsedImplicitly]
        protected Border StorageType;
        [UsedImplicitly]
        protected Scrollbox SettingsPane;

        public virtual ICreativeExchangeStorageService CreativeExchangeStorageService { get; set; }
        public virtual IEnumerable<CreativeExchangeStorageDefinition> CreativeExchangeStorageDefinitions { get; set; }

        public Item Item { get; set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Item = Support.XA.Feature.CreativeExchange.UIUtil.GetItemFromQueryString(Client.ContentDatabase);
            ContextInfo.Text = string.Format(
                Translate.Text(CreativeExchangeFeature.Texts.YouAreAboutToImportTheVersionOfTheSite),
                Item.Language.GetDisplayName(), ServiceLocator.Current.Resolve<ISiteInfoResolver>().GetSiteInfo(Item).Name);

            AddStorageTypeRadio(Item);
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

        protected virtual void AddStorageTypeRadio(Item home)
        {
            CreativeExchangeStorageService = ServiceLocator.Current.Resolve<ICreativeExchangeStorageService>();
            CreativeExchangeStorageDefinitions = CreativeExchangeStorageService.GetStorageServicesDefinitions(home);

            if (CreativeExchangeStorageDefinitions == null)
            {
                ContextInfo.Text = CreativeExchangeFeature.Texts.CouldNotFindAnyStorageDefinitionInYourSiteImportCannotBePerformed;
                NextButton.Disabled = true;
                SettingsPane.Visible = false;
            }
            else
            {
                StorageType.AddRadioButtons(CreativeExchangeStorageDefinitions, i => i.Name, i => i.Item.ID.ToString(), "rbStorageType");
            }
        }


        protected override void ActivePageChanged(string page, string oldPage)
        {
            base.ActivePageChanged(page, oldPage);

            if (page == "SelectPackage")
            {
                var rawValue = HttpContext.Current.Request["rbStorageType"];
                var creativeExchangeStorageDefinition = CreativeExchangeStorageDefinitions.FirstOrDefault(c => c.Item.ID.Equals(new ID(rawValue)));
                var importMethod = creativeExchangeStorageDefinition?.InstantiateImportStorage()?.GetImportMethod();
                if (importMethod == null)
                {
                    SheerResponse.Alert(CreativeExchangeFeature.Texts.CouldNotInstantiateImportStorage);
                    NextButton.Disabled = true;
                    return;
                }
                if (importMethod == ImportMethod.Upload)
                {
                    NextButton.Disabled = true;
                    Context.ClientPage.SendMessage(this, "importsite:loadpackages");
                }
                else
                {
                    SkipPage(page, oldPage);
                }
            }
            else if (page == "Importing")
            {
                NextButton.Disabled = true;
                BackButton.Disabled = true;
                CancelButton.Disabled = true;
                Context.ClientPage.SendMessage(this, "importsite:import");
            }
            else if (page == "Results")
            {
                BackButton.Disabled = true;
            }
        }

        [HandleMessage("importsite:loadpackages")]
        protected void OnLoadPackages(Message message)
        {
            string directory = Settings.PackagePath + @"\CreativeExchange";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PackageList.Controls.Clear();
            foreach (string path in Directory.GetFiles(directory, "*.zip"))
            {
                FileInfo file = new FileInfo(path);
                ListviewItem item = new ListviewItem { ID = Control.GetUniqueID("LI") };
                Context.ClientPage.AddControl(PackageList, item);
                item.Header = file.Name;
                item.Value = file.FullName;
                item.Icon = "People/32x32/package.png";

                if (file.Name == message.Arguments["select"])
                {
                    item.Selected = true;
                    SelectPackageListItem();
                }
            }
            PackageList.RefreshListview();
        }

        [HandleMessage("importsite:uploadpackage")]
        protected void OnUploadPackage(Message message)
        {
            ClientPipelineArgs args = new ClientPipelineArgs();
            Context.ClientPage.Start(this, "UploadPackage", args);
        }

        protected void UploadPackage(ClientPipelineArgs args)
        {
            if (!args.IsPostBack)
            {
                UploadPackageForm.Show(Settings.PackagePath + @"\CreativeExchange", true);
                args.WaitForPostBack();
            }
            else
            {
                if (args.Result != null && args.Result.StartsWith("ok:"))
                {
                    string message = "importsite:loadpackages(select=" + HttpUtility.UrlEncode(args.Result.Substring(3)) + ")";
                    Context.ClientPage.SendMessage(this, message);
                }
                else
                {
                    Context.ClientPage.SendMessage(this, "importsite:loadpackages");
                }
            }
        }

        [HandleMessage("importsite:downloadpackage")]
        protected void OnDownloadPackage(Message message)
        {
            this.LogInfo("OnDownloadPackage:begin");
            ListviewItem[] selectedItems = PackageList.SelectedItems;
            if (selectedItems == null || selectedItems.Length != 1)
            {
                SheerResponse.Alert(Texts.SelectAFileToDownload);
            }
            else
            {
                SheerResponse.Download(selectedItems[0].Value);
            }
            this.LogInfo("OnDownloadPackage:end");
        }

        [HandleMessage("importsite:import")]
        protected void OnImport(Message message)
        {
            var args = new GetImportContextArgs
            {
                ImportContext = new ImportContext(),
                Item = Item,
                HttpContext = HttpContext.Current
            };

            var exportBackgroundJob = ServiceLocator.Current.Resolve<IImportBackgroundJob>();
            Monitor.Start("Import", "CreativeExchange", () => exportBackgroundJob.Run(args));
        }

        [HandleMessage("importsite:fail")]
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

        [HandleMessage("importsite:success")]
        protected void OnSuccess(Message message)
        {
            this.LogInfo("OnSuccess:begin");
            Job job = JobManager.GetJob(Monitor.JobHandle);
            ImportJobResponse response = job.Status.Result as ImportJobResponse;
            if (response == null)
            {
                return;
            }

            //if (HttpContext.Current.Request["rbSource"] == "Zip" && HttpContext.Current.Request["cbPurge"] == "1")
            //{
            //    try
            //    {
            //        File.Delete(response.ReturnValue);
            //    }
            //    catch
            //    {
            //        //Do nothing
            //    }
            //}
            HttpContext.Current.Session.Add(CreativeExchangeFeature.Constants.CreativeExchangeMessages, response.Messages);
            foreach (MessageBase entry in response.Messages)
            {
                ListviewItem item = entry.GetListviewItem();
                Context.ClientPage.AddControl(MessageList, item);
                item.ID = Control.GetUniqueID("I");
            }

            Success.Visible = true;
            Active = "Results";

            if (response.Messages.Count == 0)
            {
                Next();
                BackButton.Disabled = true;
            }
            else
            {
                MessageList.RefreshListview();
            }
            this.LogInfo("OnSuccess:end");
        }

        private void SkipPage(string page, string oldPage)
        {
            if (Pages.IndexOf(page) > Pages.IndexOf(oldPage))
            {
                Next();
            }
            else
            {
                Back();
            }
        }

        protected void SelectPackageListItem()
        {
            ListviewItem[] selectedItems = PackageList.SelectedItems;
            if (selectedItems.Length > 0)
            {
                PackageName.Value = selectedItems[0].Header;
                NextButton.Disabled = false;
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

        private void LogInfo(string message)
        {
            Log.Info(message, this);
        }
    }
}
