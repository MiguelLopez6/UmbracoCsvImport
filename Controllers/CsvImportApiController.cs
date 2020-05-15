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

                    if (variant.PropertyTypes != null)
                        foreach (var prop in variant.PropertyTypes)
                            if (prop.AllowVaryingByCulture)
                                this.SetPropertyValue(content, prop, variant.Language.CultureInfo);
                            else
                                this.SetPropertyValue(content, prop, null);
                }

                if (model.Page.AllowVaryingByCulture)
                    cs.SaveAndPublish(content, defaultVariant.Language.CultureInfo);
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
            var properties = contentType.CompositionPropertyTypes;

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
                    PropertyTypes = new List<Models.PropertyType>()
                };

                foreach (var prop in properties)
                {
                    var propAllowVaryingByCulture = prop.Variations.Equals(ContentVariation.Culture);

                    if (!lang.IsDefault && !propAllowVaryingByCulture)
                    { }
                    else
                    {
                        var propType = new Models.PropertyType();
                        propType.Alias = prop.Alias;
                        propType.Name = prop.Name;
                        propType.AllowVaryingByCulture = propAllowVaryingByCulture;
                        variant.PropertyTypes.Add(propType);
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