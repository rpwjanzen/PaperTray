using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace PaperTray.Services;

public sealed class RazorViewRenderService : IViewRenderService
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RazorViewRenderService(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> RenderPartialToStringAsync<TModel>(string viewPath, TModel model)
    {
        HttpContext httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("A current HTTP context is required to render a view.");

        ActionContext actionContext = new(httpContext, httpContext.GetRouteData(), new ActionDescriptor());
        IView view = FindView(viewPath, actionContext);

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };
        var viewContext = new ViewContext(
            actionContext,
            view,
            viewData,
            new TempDataDictionary(httpContext, _tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await view.RenderAsync(viewContext);
        return writer.ToString();
    }

    private IView FindView(string viewPath, ActionContext actionContext)
    {
        ViewEngineResult viewResult = _viewEngine.GetView(
            executingFilePath: null,
            viewPath,
            isMainPage: false);

        if (!viewResult.Success)
        {
            viewResult = _viewEngine.FindView(actionContext, viewPath, isMainPage: false);
        }

        if (!viewResult.Success)
        {
            string searchedLocations = string.Join(Environment.NewLine, viewResult.SearchedLocations);
            throw new InvalidOperationException(
                $"Unable to find the view '{viewPath}'. Searched locations:{Environment.NewLine}{searchedLocations}");
        }

        return viewResult.View;
    }
}
