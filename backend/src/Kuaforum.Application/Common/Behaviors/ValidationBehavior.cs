using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using Kuaforum.Domain.Common;
using MediatR;

namespace Kuaforum.Application.Common.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    // T10 fix: (TResponse)(object)Result.Failure(error) sadece TResponse == Result iken güvenli.
    // TResponse == Result<T> olan (yani ICommand<T> kullanan) HER command'da bu cast çalışma zamanı
    // tipi eşleşmediği için InvalidCastException fırlatıyordu → validation hatası 400 yerine 500
    // dönüyordu. Generic Result.Failure<T>(Error) overload'ı reflection ile TResponse'un T'siyle
    // resolve edilip cache'lenir (validation-fail yolu sıcak değil ama reflection'ı her istekte
    // tekrarlamaya gerek yok).
    private static readonly ConcurrentDictionary<Type, MethodInfo> FailureFactoryCache = new();

    private static readonly MethodInfo GenericFailureDefinition = typeof(Result)
        .GetMethods()
        .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition);

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var error = Error.Validation(
            string.Join(" | ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

        return CreateFailure(error);
    }

    /// <summary>
    /// TResponse == Result → Result.Failure(error) doğrudan kullanılır (boxed cast güvenli).
    /// TResponse == Result&lt;T&gt; → runtime tipi Result&lt;T&gt; olmalı; bunun için generic
    /// Result.Failure&lt;T&gt;(error) overload'ı TResponse'un jenerik argümanıyla resolve edilir.
    /// </summary>
    private static TResponse CreateFailure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var factory = FailureFactoryCache.GetOrAdd(typeof(TResponse), static responseType =>
            {
                var valueType = responseType.GetGenericArguments()[0];
                return GenericFailureDefinition.MakeGenericMethod(valueType);
            });

            return (TResponse)factory.Invoke(null, [error])!;
        }

        // TResponse constraint'i (: Result) pratikte sadece Result ve Result<T>'yi mümkün kılıyor;
        // teorik üçüncü bir alt tip gelirse sessiz cast hatası yerine anlamlı exception fırlat.
        throw new InvalidOperationException(
            $"ValidationBehavior yalnızca {nameof(Result)} veya {nameof(Result)}<T> response tipini destekler, gelen: {typeof(TResponse).Name}");
    }
}
