namespace PaperTray.Services;

public interface IViewRenderService
{
    Task<string> RenderPartialToStringAsync<TModel>(string viewPath, TModel model);
}
