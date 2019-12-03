﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using SiteServer.BackgroundPages.Cms;
using SiteServer.CMS.Context;
using SiteServer.Abstractions;
using SiteServer.CMS.Core;
using SiteServer.CMS.Repositories;

namespace SiteServer.API.Controllers.Pages.Settings.Site
{
    
    [RoutePrefix("pages/settings/siteAdd")]
    public class PagesSiteAddController : ApiController
    {
        private const string Route = "";

        [HttpGet, Route(Route)]
        public async Task<IHttpActionResult> GetConfig()
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                if (!request.IsAdminLoggin ||
                    !await request.AdminPermissionsImpl.HasSystemPermissionsAsync(Constants.SettingsPermissions.SiteAdd))
                {
                    return Unauthorized();
                }

                var siteTemplates = SiteTemplateManager.Instance.GetSiteTemplateSortedList();

                var siteList = new List<KeyValuePair<int, string>>
                {
                    new KeyValuePair<int, string>(0, "<无上级站点>")
                };

                var siteIdList = await DataProvider.SiteRepository.GetSiteIdListAsync();
                var siteInfoList = new List<Abstractions.Site>();
                var parentWithChildren = new Dictionary<int, List<Abstractions.Site>>();
                foreach (var siteId in siteIdList)
                {
                    var site = await DataProvider.SiteRepository.GetAsync(siteId);
                    if (site.ParentId == 0)
                    {
                        siteInfoList.Add(site);
                    }
                    else
                    {
                        var children = new List<Abstractions.Site>();
                        if (parentWithChildren.ContainsKey(site.ParentId))
                        {
                            children = parentWithChildren[site.ParentId];
                        }
                        children.Add(site);
                        parentWithChildren[site.ParentId] = children;
                    }
                }
                foreach (Abstractions.Site site in siteInfoList)
                {
                    AddSite(siteList, site, parentWithChildren, 0);
                }

                var tableNameList = await DataProvider.SiteRepository.GetSiteTableNamesAsync();

                var rootExists = await DataProvider.SiteRepository.GetSiteByIsRootAsync() != null;

                return Ok(new
                {
                    Value = siteTemplates.Values,
                    RootExists = rootExists,
                    SiteList = siteList,
                    TableNameList = tableNameList
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        private static void AddSite(List<KeyValuePair<int, string>> siteList, Abstractions.Site site, Dictionary<int, List<Abstractions.Site>> parentWithChildren, int level)
        {
            if (level > 1) return;
            var padding = string.Empty;
            for (var i = 0; i < level; i++)
            {
                padding += "　";
            }
            if (level > 0)
            {
                padding += "└ ";
            }

            if (parentWithChildren.ContainsKey(site.Id))
            {
                var children = parentWithChildren[site.Id];
                siteList.Add(new KeyValuePair<int, string>(site.Id, padding + site.SiteName + $"({children.Count})"));
                level++;
                foreach (var subSite in children)
                {
                    AddSite(siteList, subSite, parentWithChildren, level);
                }
            }
            else
            {
                siteList.Add(new KeyValuePair<int, string>(site.Id, padding + site.SiteName));
            }
        }

        [HttpPost, Route(Route)]
        public async Task<IHttpActionResult> Submit()
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                if (!request.IsAdminLoggin ||
                    !await request.AdminPermissionsImpl.HasSystemPermissionsAsync(Constants.SettingsPermissions.SiteAdd))
                {
                    return Unauthorized();
                }

                var createType = request.GetPostString("createType");
                var createTemplateId = request.GetPostString("createTemplateId");
                var siteName = request.GetPostString("siteName");
                var root = request.GetPostBool("root");
                var parentId = request.GetPostInt("parentId");
                var siteDir = request.GetPostString("siteDir");
                var tableRule = ETableRuleUtils.GetEnumType(request.GetPostString("tableRule"));
                var tableChoose = request.GetPostString("tableChoose");
                var tableHandWrite = request.GetPostString("tableHandWrite");
                var isImportContents = request.GetPostBool("isImportContents");
                var isImportTableStyles = request.GetPostBool("isImportTableStyles");

                if (!root)
                {
                    if (WebUtils.IsSystemDirectory(siteDir))
                    {
                        return BadRequest("文件夹名称不能为系统文件夹名称，请更改文件夹名称！");
                    }
                    if (!DirectoryUtils.IsDirectoryNameCompliant(siteDir))
                    {
                        return BadRequest("文件夹名称不符合系统要求，请更改文件夹名称！");
                    }
                    var list = await DataProvider.SiteRepository.GetLowerSiteDirListAsync(parentId);
                    if (list.Contains(siteDir.ToLower()))
                    {
                        return BadRequest("已存在相同的发布路径，请更改文件夹名称！");
                    }
                }

                var channelInfo = new Channel();

                channelInfo.ChannelName = channelInfo.IndexName = "首页";
                channelInfo.ParentId = 0;
                channelInfo.ContentModelPluginId = string.Empty;

                var tableName = string.Empty;
                if (tableRule == ETableRule.Choose)
                {
                    tableName = tableChoose;
                }
                else if (tableRule == ETableRule.HandWrite)
                {
                    tableName = tableHandWrite;
                    
                    if (!await WebConfigUtils.Database.IsTableExistsAsync(tableName))
                    {
                        await DataProvider.ContentRepository.CreateContentTableAsync(tableName, DataProvider.ContentRepository.GetDefaultTableColumns(tableName));
                    }
                    else
                    {
                        await DataProvider.DatabaseRepository.AlterSystemTableAsync(tableName, DataProvider.ContentRepository.GetDefaultTableColumns(tableName));
                    }
                }

                var site = new Abstractions.Site
                {
                    SiteName = siteName,
                    SiteDir = siteDir,
                    TableName = tableName,
                    ParentId = parentId,
                    Root = root
                };
                site.IsCheckContentLevel = false;
                site.Charset = ECharsetUtils.GetValue(ECharset.utf_8);

                var siteId = await DataProvider.ChannelRepository.InsertSiteAsync(channelInfo, site, request.AdminName);

                if (string.IsNullOrEmpty(tableName))
                {
                    tableName = ContentRepository.GetContentTableName(siteId);
                    await DataProvider.ContentRepository.CreateContentTableAsync(tableName, DataProvider.ContentRepository.GetDefaultTableColumns(tableName));
                    await DataProvider.SiteRepository.UpdateTableNameAsync(siteId, tableName);
                }

                if (await request.AdminPermissionsImpl.IsSiteAdminAsync() && !await request.AdminPermissionsImpl.IsSuperAdminAsync())
                {
                    var siteIdList = await request.AdminPermissionsImpl.GetSiteIdListAsync() ?? new List<int>();
                    siteIdList.Add(siteId);
                    var adminInfo = await DataProvider.AdministratorRepository.GetByUserIdAsync(request.AdminId);
                    await DataProvider.AdministratorRepository.UpdateSiteIdCollectionAsync(adminInfo, siteIdList);
                }

                var siteTemplateDir = string.Empty;
                var onlineTemplateName = string.Empty;
                if (StringUtils.EqualsIgnoreCase(createType, "local"))
                {
                    siteTemplateDir = createTemplateId;
                }
                else if (StringUtils.EqualsIgnoreCase(createType, "cloud"))
                {
                    onlineTemplateName = createTemplateId;
                }

                var redirectUrl = PageProgressBar.GetCreateSiteUrl(siteId,
                    isImportContents, isImportTableStyles, siteTemplateDir, onlineTemplateName, StringUtils.Guid());

                return Ok(new
                {
                    Value = redirectUrl
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}