/*
 * Copyright (c) 2019-2021 Håkan Edling
 *
 * This software may be modified and distributed under the terms
 * of the MIT license.  See the LICENSE file for details.
 *
 * http://github.com/tidyui/statica
 *
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Piranha;
using Statica.Models;

namespace Statica.Services
{
    public class StructureService : IStructureService
    {
        // The unique id

        // The optional title

        // The base slug for the structure
        private readonly string _baseSlug;

        /// The base path for the structure.
        private readonly string _dataPath;

        /// The current sitemap.
        private StaticSitemap _structure;

        /// Mutex for initialization.
        private readonly object _mutex = new();

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="id">The unique id</param>
        /// <param name="baseSlug">The base slug</param>
        /// <param name="dataPath">The data path</param>
        /// <param name="title">The optional title</param>
        public StructureService(string id, string baseSlug, string dataPath, string title = null)
        {
            Id = id;
            _baseSlug = baseSlug;
            _dataPath = dataPath;
            Title = title;

            if (!string.IsNullOrWhiteSpace(_baseSlug) && !_baseSlug.EndsWith("/"))
                _baseSlug += "/";
        }

        /// <summary>
        /// Gets the unique structure id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the optional title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the current page structure.
        /// </summary>
        public StaticSitemap Sitemap
        {
            get
            {
                switch (_structure)
                {
                    // Check if the map has already been loaded
                    case null:
                    {
                        // Lock for thread safety
                        lock (_mutex)
                        {
                            if (_structure == null)
                            {
                                // Get the static map
                                var structure = new StaticSitemap();
                                structure.Items = GetStructureItemsRecursive(structure, _dataPath, _baseSlug);

                                _structure = structure;
                            }
                        }

                        break;
                    }
                }

                return _structure;
            }
        }

        /// <summary>
        /// Gets the page with the given slug.
        /// </summary>
        /// <param name="slug">The slug</param>
        /// <returns>The page</returns>
        public async Task<StaticPageModel> GetPageAsync(string slug)
        {
            switch (string.IsNullOrWhiteSpace(slug))
            {
                case false:
                {
                    var key = _baseSlug + slug;

                    if (Sitemap.Routes.TryGetValue(key, out var page))
                    {
                        var model = new StaticPageModel
                        {
                            Title = page.Title,
                            Slug = page.Slug,
                            Path = page.Path,
                            Redirect = page.Redirect,
                            Created = page.Created,
                            LastModified = page.LastModified
                        };

                        if (!string.IsNullOrWhiteSpace(model.Redirect)) return model;
                        var file = new FileInfo(page.Path);
                        using var sr = new StreamReader(file.OpenRead());
                        model.Markdown = await sr.ReadToEndAsync();
                        model.Body = App.Markdown.Transform(model.Markdown);
                        return model;
                    }

                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Reloads the structure information.
        /// </summary>
        public Task Reload()
        {
            return Task.Run(() =>
            {
                _structure = null;
            });
        }

        /// <summary>
        /// Gets the page structure.
        /// </summary>
        /// <param name="sitemap"></param>
        /// <param name="path"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        private IList<StaticPage> GetStructureItemsRecursive(StaticSitemap structure, string path, string prefix = "")
        {
            var items = new List<StaticPage>();

            foreach (var info in GetDirectoryItems(path))
            {
                if (info.Name != "Index.md")
                {
                    var item = new StaticPage
                    {
                        Title = Utils.GenerateTitle(info),
                        Slug = prefix + Utils.GenerateSlug(info),
                        Path = info.FullName,
                        Created = info.CreationTime,
                        LastModified = info.LastWriteTime
                    };

                    // Check if this is a directory
                    if (info is DirectoryInfo dir)
                    {
                        // Get the subitems
                        item.Items = GetStructureItemsRecursive(structure, dir.FullName, $"{item.Slug}/");

                        // Check if the directory has an index file
                        var index = dir.GetFiles("Index.md").FirstOrDefault();
                        if (index != null)
                        {
                            item.Path = index.FullName;
                            item.Created = index.CreationTime;
                            item.LastModified = index.LastWriteTime;
                        }
                        else if (item.Items.Count > 0)
                        {
                            item.Path = null;
                            item.Redirect = item.Items[0].Slug;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    structure.Routes[item.Slug] = item;
                    items.Add(item);
                }
            }
            return items;
        }

        /// <summary>
        /// Gets the items in the specified directory that should
        /// be included in the sitemap.
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns>The items</returns>
        private IEnumerable<FileSystemInfo> GetDirectoryItems(string path)
        {
            var dir = new DirectoryInfo(path);
            var items = new List<FileSystemInfo>();

            items.AddRange(dir.GetDirectories().Where(d => !d.Name.StartsWith("_")).ToList());
            items.AddRange(dir.GetFiles("*.md"));

            return items.OrderBy(i => i.Name).ToList();
        }
    }
}