using HomeLedger.Core.Utilities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HomeLedger.Web.ModelBinding;

public class HomeLedgerDateModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
        var modelType = bindingContext.ModelType;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (Nullable.GetUnderlyingType(modelType) is not null)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Date is required (yyyy/MM/dd).");

            return Task.CompletedTask;
        }

        if (!HomeLedgerFormats.TryParseDate(value, out var date))
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Use date format yyyy/MM/dd.");
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(date);
        return Task.CompletedTask;
    }
}

public class HomeLedgerDateModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(DateOnly) || context.Metadata.ModelType == typeof(DateOnly?))
            return new HomeLedgerDateModelBinder();

        return null;
    }
}
