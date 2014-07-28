﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using Acuerdo.External.Shortener;
using Acuerdo.External.Uploader;
using Acuerdo.Plugin;
using Inscribe.Filter;
using Inscribe.Filter.Core;
using Inscribe.Storage;

namespace Inscribe.Plugin
{
    internal static class PluginLoader
    {
        public static void Load()
        {
            var pload = new PluginLoadExecuter();
            if(pload.plugins != null)
                foreach (var p in pload.plugins)
                {
                    PluginManager.Register(p);
                }

            if(pload.filters != null)
                foreach (var f in pload.filters)
                {
                    FilterRegistrant.RegisterFilter(f.GetType());
                }

            if(pload.uploaders != null)
                foreach (var u in pload.uploaders)
                {
                    UploaderManager.RegisterUploader(u);
                }

            if(pload.resolvers != null)
                foreach (var r in pload.resolvers)
                {
                    UploaderManager.RegisterResolver(r);
                }

            // shorteners are no longer supported.
            /*
            if(pload.shorteners != null)
                foreach (var s in pload.shorteners)
                {
                    ShortenManager.RegisterShortener(s);
                }
            */

            if(pload.extractors != null)
                foreach (var e in pload.extractors)
                {
                    ShortenManager.RegisterExtractor(e);
                }
        }

        private class PluginLoadExecuter
        {
            [ImportMany()]
            public List<IPlugin> plugins = null;

            [ImportMany()]
            public List<IFilter> filters = null;

            [ImportMany()]
            public List<IUploader> uploaders = null;

            [ImportMany()]
            public List<IResolver> resolvers = null;

            [ImportMany()]
            public List<IUrlShortener> shorteners = null;

            [ImportMany()]
            public List<IUriExtractor> extractors = null;

            public PluginLoadExecuter()
            {
                try
                {
                    var catalog = new DirectoryCatalog(Path.Combine(Path.GetDirectoryName(Define.ExeFilePath), "plugins"));
                    var container = new CompositionContainer(catalog);
                    container.ComposeParts(this);
                }
                catch (Exception e)
                {
                    ExceptionStorage.Register(e, ExceptionCategory.PluginError);
                }
            }
        }
    }
}
