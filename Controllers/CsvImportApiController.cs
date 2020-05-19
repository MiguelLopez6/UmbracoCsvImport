using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Mvc;
using Umbraco.Core.Models;
using Umbraco.Web.WebApi;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using UmbracoCsvImport.Models;

namespace UmbracoCsvImport.Controllers
{
    public class CsvImportApiController : UmbracoAuthorizedApiController
    {
        private static readonly string[] SkipEditors = {"Our.Umbraco.Matryoshka.GroupSeparator"};

        private static readonly string LeafletMapEditor = "Vizioz.LeafletMap";

        private static readonly string LatitudePropertyName = "Map Latitude";

        private static readonly string LongitudePropertyName = "Map Longitude";

        [HttpPost]
        public HttpResponseMessage Publish(ImportData model)
        {
            var cs = Services.ContentService;
            var cts = Services.ContentTypeService;
            
            try
            {
                var defaultVariant = model.Page.Variants.FirstOrDefault(variant => variant.Language.IsDefault);
                var content = new Content(
                        defaultVariant?.Language.Value,
                        model.ParentId,
                        cts.Get(model.ContentTypeAlias));

                foreach (var variant in model.Page.Variants)
                {
                    if (model.Page.AllowVaryingByCulture)
                        content.SetCultureName(variant.Language.Value, variant.Language.CultureInfo);

                    var propertyTypes = variant.PropertyGroups?.SelectMany(group => group.PropertyTypes).ToList();

                    if (propertyTypes != null && propertyTypes.Any())
                    {
                        foreach (var prop in propertyTypes)
                        {
                            this.SetPropertyValue(content, prop, prop.AllowVaryingByCulture ? variant.Language.CultureInfo : null);
                        }

                        // Get lat and lon values for custom map property
                        var lat = propertyTypes.FirstOrDefault(x => x.Name == LatitudePropertyName);
                        var lon = propertyTypes.FirstOrDefault(x => x.Name == LongitudePropertyName);
                        var zoom = lat != null && lon != null ? 13 : 2;

                        var mapProperty = new Models.PropertyType
                        {
                            Alias = lat?.Alias,
                            AllowVaryingByCulture = lat?.AllowVaryingByCulture ?? false,
                            Value = $"{{\"latLng\":[{lat?.Value},{lon?.Value}],\"zoom\":{zoom}}}"
                        };
                        this.SetPropertyValue(content, mapProperty, mapProperty.AllowVaryingByCulture ? variant.Language.CultureInfo : null);
                    }
                }

                if (model.Page.AllowVaryingByCulture)
                    cs.SaveAndPublish(content, defaultVariant.Language.CultureInfo, raiseEvents: false);
                else
                    cs.SaveAndPublish(content);

                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpGet]
        public HttpResponseMessage GetModel(int contentTypeId)
        {
            var page = new Page();
            var contentType = Services.ContentTypeService.Get(contentTypeId);

            var propertyGroups = contentType.CompositionPropertyGroups.OrderBy(group => group.SortOrder)
                .GroupBy(group => group.Name).ToList();

            page.Variants = new List<Variant>();
            page.AllowVaryingByCulture = contentType.Variations.Equals(ContentVariation.Culture);

            List<ILanguage> languages;
            if (page.AllowVaryingByCulture)
                languages = Services.LocalizationService.GetAllLanguages().ToList();
            else
                languages = Services.LocalizationService.GetAllLanguages().Where(lang => lang.IsDefault).ToList();
            
            foreach (var lang in languages)
            {
                var language = new Models.Language()
                {
                    CultureName = lang.CultureName,
                    IsDefault = lang.IsDefault,
                    CultureInfo = lang.CultureInfo.ToString()
                };

                var variant = new Variant()
                {
                    Language = language,
                    PropertyGroups = new List<Models.PropertyGroup>()
                };

                foreach (var group in propertyGroups)
                {
                    var propGroup = new Models.PropertyGroup
                    {
                        Name = group.Key,
                        PropertyTypes = new List<Models.PropertyType>()
                    };

                    var groupPropertyTypes = group.SelectMany(x => x.PropertyTypes).OrderBy(p => p.SortOrder);

                    foreach (var prop in groupPropertyTypes)
                    {
                        if (SkipEditors.Contains(prop.PropertyEditorAlias))
                        {
                            continue;
                        }

                        var propAllowVaryingByCulture = prop.Variations.Equals(ContentVariation.Culture);

                        if (!lang.IsDefault && !propAllowVaryingByCulture)
                        { }
                        else
                        {
                            // Let parsing lat and lon values separately for custom map property
                            if (prop.PropertyEditorAlias == LeafletMapEditor)
                            {
                                var lat = new Models.PropertyType
                                {
                                    Alias = prop.Alias,
                                    Name = LatitudePropertyName,
                                    AllowVaryingByCulture = propAllowVaryingByCulture
                                };
                                propGroup.PropertyTypes.Add(lat);

                                var lon = new Models.PropertyType
                                {
                                    Alias = prop.Alias,
                                    Name = LongitudePropertyName,
                                    AllowVaryingByCulture = propAllowVaryingByCulture
                                };
                                propGroup.PropertyTypes.Add(lon);
                            }
                            else
                            {
                                var propType = new Models.PropertyType();
                                propType.Alias = prop.Alias;
                                propType.Name = prop.Name;
                                propType.AllowVaryingByCulture = propAllowVaryingByCulture;
                                propGroup.PropertyTypes.Add(propType);
                            }
                        }
                    }

                    if (propGroup.PropertyTypes.Any())
                    {
                        variant.PropertyGroups.Add(propGroup);
                    }
                }

                page.Variants.Add(variant);
            }

            return Request.CreateResponse(HttpStatusCode.OK, page);
        }

        private void SetPropertyValue(IContent content, Models.PropertyType prop, string culture)
        {
            if (content.HasProperty(prop.Alias))
            {
                var property = content.Properties[prop.Alias];
                var value = this.FormatPropertyValueForEditor(property, prop.Value);

                if (value != null)
                {
                    content.SetValue(prop.Alias, value, culture: culture);
                }
            }
        }

        private object FormatPropertyValueForEditor(Property property, string propValue)
        {
            switch (property.PropertyType.PropertyEditorAlias)
            {
                case "Umbraco.CheckBoxList":
                case "Umbraco.DropDown.Flexible":
                    return this.FormatMultipleSelectionValue(propValue);
                case "Umbraco.ColorPicker":
                    return this.FormatColorPickerValue(propValue);
                case "Umbraco.ContentPicker":
                case "Umbraco.MediaPicker":
                case "Umbraco.MemberPicker":
                case "Umbraco.MultiNodeTreePicker":
                    return this.FormatPickerValue(property, propValue);
                case "Umbraco.TrueFalse":
                    return this.FormatTrueFalseValue(propValue);
                default:
                    return propValue;
            }
        }

        private int FormatTrueFalseValue(string propValue)
        {
            if (bool.TryParse(propValue, out var value))
            {
                return value ? 1 : 0;
            }
            else if (propValue.Trim().Equals("yes", StringComparison.InvariantCultureIgnoreCase))
            {
                return 1;
            }
            else if (propValue.Trim().Equals("no", StringComparison.InvariantCultureIgnoreCase))
            {
                return 0;
            }
            else
            {
                return int.Parse(propValue);
            }
        }

        private string FormatMultipleSelectionValue(string propValue)
        {
            var values = propValue.Split(',').Select(x => x.Trim());

            return JsonConvert.SerializeObject(values);
        }

        private string FormatColorPickerValue(string propValue)
        {
            if (propValue.StartsWith("#"))
            {
                propValue = propValue.TrimStart('#');
            }

            return propValue;
        }

        private string FormatPickerValue(Property property, string propValue)
        {
            var maxItems = 1;
            var dataType = Services.DataTypeService.GetDataType(property.PropertyType.DataTypeId);

            if (dataType != null)
            {
                var config = JObject.FromObject(dataType.Configuration);

                if (config.ContainsKey("Multiple"))
                {
                    maxItems = config.Value<bool>("Multiple") ? 0 : 1;
                }
                else if (config.ContainsKey("MaxNumber"))
                {
                    maxItems = config.Value<int>("MaxNumber");
                }
            }

            var result = new List<Udi>();
            var values = propValue.Split(',');

            foreach (var val in values)
            {
                if (GuidUdi.TryParse(val, out GuidUdi udi))
                {
                    result.Add(udi);
                }
            }

            var take = maxItems > 0 ? result.Take(maxItems).ToList() : result;

            return string.Join(",", take.Select(x => x.ToString()));
        }
    }
}