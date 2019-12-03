﻿using System.Threading.Tasks;
using System.Web.Http;
using SiteServer.CMS.Api.Sys.Packaging;
using SiteServer.CMS.Context;
using SiteServer.CMS.Core;
using SiteServer.CMS.Packaging;
using SiteServer.Abstractions;

namespace SiteServer.API.Controllers.Sys
{
    
    public class SysPackagesSyncDatabaseController : ApiController
    {
        [HttpPost, Route(ApiRouteSyncDatabase.Route)]
        public async Task<IHttpActionResult> Main()
        {
            var idWithVersion = $"{PackageUtils.PackageIdSsCms}.{SystemManager.ProductVersion}";
            var packagePath = WebUtils.GetPackagesPath(idWithVersion);
            var homeDirectory = PathUtils.GetHomeDirectoryPath(string.Empty);
            if (!DirectoryUtils.IsDirectoryExists(homeDirectory) || !FileUtils.IsFileExists(PathUtils.Combine(homeDirectory, "config.js")))
            {
                DirectoryUtils.Copy(PathUtils.Combine(packagePath, DirectoryUtils.Home.DirectoryName), homeDirectory, true);
            }

            await SystemManager.SyncDatabaseAsync();

            return Ok(new
            {
                Version = SystemManager.ProductVersion
            });
        }
    }
}