using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Mvc;
using Umbraco.Core.Models;
using Umbraco.Web.WebApi;
using System.Net;
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

                    var propertyTypes = variant.PropertyGroups?.SelectMany(group => group.PropertyTypes).ToList();

                    if (propertyTypes != null && propertyTypes.Any())
                        foreach (var prop in propertyTypes)
                            if (prop.AllowVaryingByCulture)
                                content.SetValue(prop.Alias, prop.Value, culture: variant.Language.CultureInfo);
                            else
                                content.SetValue(prop.Alias, prop.Value);
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

            var propertyGroups = contentType.CompositionPropertyGroups.OrderBy(group => group.SortOrder)
                .GroupBy(group => group.Name);

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
                        var propAllowVaryingByCulture = prop.Variations.Equals(ContentVariation.Culture);

                        if (!lang.IsDefault && !propAllowVaryingByCulture)
                        { }
                        else
                        {
                            var propType = new Models.PropertyType();
                            propType.Alias = prop.Alias;
                            propType.Name = prop.Name;
                            propType.AllowVaryingByCulture = propAllowVaryingByCulture;
                            propGroup.PropertyTypes.Add(propType);
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
    }
}