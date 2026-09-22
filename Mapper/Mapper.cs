using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapper;

public static class Mapper
{
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> Cache = new();
    
    public static TTarget Map<TTarget>(this object source) where TTarget : new()
    {
        Type sourceType = source.GetType(); 

        Func<object, TTarget> mapper = (Func<object, TTarget>)Cache.GetOrAdd(
            (sourceType, typeof(TTarget)), 
            _ => CreateMapFunction<TTarget>(sourceType));
        
        return mapper(source);
    }

    public static IEnumerable<TTarget> Map<TTarget>(this IEnumerable source) where TTarget : new()
    {
        Func<object, TTarget>? mapper = null;
        Type? lastType = null;

        foreach (Type item in source)
        {
            if (item is null) 
            {
                yield return default!;
                continue;
            }

            Type currentType = item.GetType();
            
            if (mapper == null || lastType != currentType)
            {
                lastType = currentType;
                mapper = (Func<object, TTarget>) Cache.GetOrAdd(
                    (currentType, typeof(TTarget)), 
                    _ => CreateMapFunction<TTarget>(currentType));
            }

            yield return mapper(item);
        }
    }

    private static Func<object, TTarget> CreateMapFunction<TTarget>(Type sourceType) where TTarget : new()
    {
        ParameterExpression sourceParameter = Expression.Parameter(typeof(object), "source");
        UnaryExpression castSource = Expression.Convert(sourceParameter, sourceType);
        
        MemberInitExpression body = Expression.MemberInit(
            Expression.New(typeof(TTarget)), 
            CreateBindings<TTarget>(castSource, sourceType));
        
        Expression<Func<object, TTarget>> lambda = Expression.Lambda<Func<object, TTarget>>(body, sourceParameter);
        
        return lambda.Compile();
    }

    private static List<MemberBinding> CreateBindings<TTarget>(Expression sourceValue, Type sourceType)
    {
        PropertyInfo[] sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo[] targetProperties = typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        Dictionary<string, PropertyInfo> sourceMap = sourceProperties.ToDictionary(propInfo => propInfo.Name);
        List<MemberBinding> bindings = new();
        
        foreach (PropertyInfo targetProperty in targetProperties)
        {
            if (!targetProperty.CanWrite) continue;
            if (!sourceMap.TryGetValue(targetProperty.Name, out PropertyInfo? sourceProperty)) continue;
            if (sourceProperty.PropertyType != targetProperty.PropertyType) continue;

            MemberExpression propertyAccess = Expression.Property(sourceValue, sourceProperty);
            MemberBinding binding = Expression.Bind(targetProperty, propertyAccess);
            
            bindings.Add(binding);
        }

        return bindings;
    }
}