using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Security.Accounts;
using Sitecore.Sites;
using Sitecore.Web;
using System.Web;

namespace Sitecore.Support.XA.Feature.CreativeExchange
{
    public static class UIUtil
    {
        public static Item GetItemFromQueryString(Database database)
        {
            string defaultValue = string.Empty;
            User user = Context.User;
            if (user != null)
            {
                Item homeItem = GetHomeItem(user);
                if (homeItem != null)
                {
                    defaultValue = homeItem.ID.ToString();
                }
                else
                {
                    Item rootItem = database.GetRootItem();
                    if (rootItem != null)
                    {
                        defaultValue = rootItem.ID.ToString();
                    }
                }
            }
            string queryString = WebUtil.GetQueryString("id", defaultValue);
            string guery = HttpContext.Current.Request.Url.Query;
            string language = HttpUtility.ParseQueryString(guery)["lang"];
            string str4 = WebUtil.GetQueryString("vs");
            return database.Items[queryString, Language.Parse(language), Sitecore.Data.Version.Parse(str4)];
        }

        public static Item GetHomeItem(User user)
        {
            Item item = null;
            string str = user.Profile["Home"];
            Database contentDatabase = Context.ContentDatabase;
            if (!string.IsNullOrEmpty(str))
            {
                item = contentDatabase.Items[str];
            }
            SiteContext site = Context.Site;
            if ((item == null) && (site.ContentStartItem.Length > 0))
            {
                item = contentDatabase.Items[site.ContentStartPath];
            }
            if (item == null)
            {
                item = contentDatabase.Items["/sitecore/content/Home"];
            }
            if (item == null)
            {
                item = contentDatabase.Items["/sitecore/content"];
            }
            return item;
        }
    }
}
