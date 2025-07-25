using Linq.Extension.Aggregation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static Linq.Extension.LinqDynamicExtension;

namespace Linq.Extension
{
    public static class AggregationExtensions
    {
        private static IQueryable GroupByOnly<TSource>(this IQueryable<TSource> source,
           IEnumerable<string> groupByFieldNames)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (groupByFieldNames == null || !groupByFieldNames.Any())
                throw new ArgumentException("At least one field name must be provided for grouping.", nameof(groupByFieldNames));            

            var sourceType = typeof(TSource);
            var parameterExpression = Expression.Parameter(sourceType, "x");

            // --- Step 1: Create the Dynamic Grouping Key Type ----
            var groupByProperties = new List<(string Name, Type Type)>();
            var memberBindingsForGrouping = new List<MemberAssignment>();

            foreach (var colName in groupByFieldNames)
            {
                var prop = sourceType.GetProperty(colName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null)
                    throw new ArgumentException($"Grouping property '{colName}' not found on tpye '{sourceType.Name}'.");
                groupByProperties.Add((prop.Name, prop.PropertyType));
                memberBindingsForGrouping.Add(Expression.Bind(prop, Expression.Property(parameterExpression, prop)));
            }

            // Dynamically create the type for the grouping key.
            var groupByKeyType = LinqRuntimeTypeBuilder.GetDynamicType(groupByProperties.ToDictionary(tuple => tuple.Name, tuple => tuple.Type));

            IEnumerable<MemberBinding> bindings = groupByKeyType.GetFields()
                .Select(p => Expression.Bind(p, Expression.Property(parameterExpression,
                    sourceType.GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase))))
                .OfType<MemberBinding>();

            // Create a NewExpression for the grouping key (e.g., new GroupingKeyType { Prop1 = x.Prop1, ....})
            var newGroupByKeyExpression = Expression.New(groupByKeyType.GetConstructor(Type.EmptyTypes));
            var groupByKeyInitExpression = Expression.MemberInit(newGroupByKeyExpression, bindings);

            // Create the GroupBy key selector lambda: x => new GroupingKeyType { .... }
            var keySelectorLambda = Expression.Lambda(groupByKeyInitExpression, parameterExpression);

            // --- Step 2: Perform the GroupBy operation ---
            // Call source.GroupBy(keySelectorLambda)
            var groupByMethod = typeof(Queryable).GetMethods()
                .Where(m => m.Name == "GroupBy" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .Select(m => m.MakeGenericMethod(typeof(TSource), groupByKeyType))
                .Single();

            var groupedQuery = (IQueryable)groupByMethod.Invoke(null, new object[] { source, keySelectorLambda });

            var resultProperties = new List<(string Name, Type Type)>();
            resultProperties.AddRange(groupByProperties); // Add all grouping key properties

            var resultType = LinqRuntimeTypeBuilder.GetDynamicType(resultProperties.ToDictionary(tuple => tuple.Name, tuple => tuple.Type));

            // Parameter for select lambda: IGrouping<GroupingKeyType, TSource>
            var groupParameter = Expression.Parameter(typeof(IGrouping<,>).MakeGenericType(groupByKeyType, sourceType), "g");

            // Access the Key of the group
            var groupKeyExpression = Expression.Property(groupParameter, "Key");

            var newResultTypeExpression = Expression.New(resultType.GetConstructor(Type.EmptyTypes));

            List<MemberBinding> resultMemberBindings = new List<MemberBinding>();

            // Bind grouping key properties from g.Key to the result type's fields
            foreach (var propInfo in groupByProperties)
            {
                var resultField = resultType.GetField(propInfo.Name); // Get the field from the dynamically generated result type.
                var groupKeyField = groupByKeyType.GetField(propInfo.Name); // Get the field from the dynamically generated grouping key type.

                // Ensure groupKeyField is not null before using it
                if (groupKeyField == null)
                    throw new InvalidOperationException($"Field '{propInfo.Name}' not found on dynamic grouping key type '{groupByKeyType.Name}'.");

                // Access the value: g.Key.Field
                var propertyAccess = Expression.Field(groupKeyExpression, groupKeyField);
                resultMemberBindings.Add(Expression.Bind(resultField, propertyAccess));
            }

            // Bind grouping key properties to the result type

            var resultInitExpression = Expression.MemberInit(newResultTypeExpression, resultMemberBindings);

            // Create the Select lambda: g => new GroupedResultType { ... }
            var resultSelectorLambda = Expression.Lambda(resultInitExpression, groupParameter);

            // Correct the paramter type of the resultSelectorLambda to match groupedQuery's element type.
            var correctedResultSelectorType = typeof(Func<,>).MakeGenericType(groupedQuery.ElementType, typeof(object));
            var correctedResultSelector = Expression.Lambda(resultSelectorLambda, groupParameter);


            // Call Queryable.Select(groupedQuery, resultSelectorLambda)

            var selectFinalMethod = typeof(Queryable).GetMethods()
                .Where(m => m.Name == "Select" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .Select(m => m.MakeGenericMethod(groupedQuery.ElementType, resultType))
                .FirstOrDefault();

            var finalQuery = (IQueryable)selectFinalMethod.Invoke(null, new object[] { groupedQuery, correctedResultSelector });

            return finalQuery;
        }
        public static IQueryable<TSource> GroupByAggregation<TSource>(this IQueryable<TSource> source,
            GroupByAggregationInput groupByAggregation)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (groupByAggregation == null) throw new ArgumentNullException(nameof(groupByAggregation));
            if (string.IsNullOrWhiteSpace(groupByAggregation.AggregationResultFieldName))
                throw new ArgumentNullException(nameof(groupByAggregation.AggregationResultFieldName));
            if (string.IsNullOrWhiteSpace(groupByAggregation.AggregationFieldName))
                throw new ArgumentNullException(nameof(groupByAggregation.AggregationFieldName));
            if (groupByAggregation.GroupByFieldNames == null || groupByAggregation.GroupByFieldNames.Count == 0)
                throw new ArgumentNullException(nameof(groupByAggregation.GroupByFieldNames));

            if(groupByAggregation?.Search != null)
                source = source.Where(groupByAggregation.Search);

            return source.GroupByAggregation(groupByAggregation.GroupByFieldNames,
                groupByAggregation.AggregationFieldName,
                groupByAggregation.AggregationResultFieldName,
                groupByAggregation.AggregationOperation);
        }

        public static IQueryable<TSource> GroupByAggregation<TSource>(this IQueryable<TSource> source,
            IEnumerable<string> groupByFieldNames,
            string aggregationFieldName,
            string aggregationResultFieldName,
            AggregationOperationType aggregationOperation = AggregationOperationType.COUNTDISTINCT)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (groupByFieldNames == null || !groupByFieldNames.Any())
                throw new ArgumentException("At least one field name must be provided for grouping.", nameof(groupByFieldNames));
            if (string.IsNullOrWhiteSpace(aggregationFieldName))
                throw new ArgumentNullException("A field name for aggregation is required.",nameof(aggregationFieldName));

            var sourceType = typeof(TSource);
            var parameterExpression = Expression.Parameter(sourceType, "x");

            // --- Step 1: Create the Dynamic Grouping Key Type ----
            var groupByProperties = new List<(string Name, Type Type)>();
            var memberBindingsForGrouping = new List<MemberAssignment>();

            foreach (var colName in groupByFieldNames)
            {
                var prop = sourceType.GetProperty(colName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null)
                    throw new ArgumentException($"Grouping property '{colName}' not found on tpye '{sourceType.Name}'.");
                groupByProperties.Add((prop.Name, prop.PropertyType));
                memberBindingsForGrouping.Add(Expression.Bind(prop, Expression.Property(parameterExpression, prop)));
            }

            // Dynamically create the type for the grouping key.
            var groupByKeyType = LinqRuntimeTypeBuilder.GetDynamicType(groupByProperties.ToDictionary(tuple => tuple.Name, tuple => tuple.Type));

            IEnumerable<MemberBinding> bindings = groupByKeyType.GetFields()
                .Select(p => Expression.Bind(p, Expression.Property(parameterExpression,
                    sourceType.GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase))))
                .OfType<MemberBinding>();

            // Create a NewExpression for the grouping key (e.g., new GroupingKeyType { Prop1 = x.Prop1, ....})
            var newGroupByKeyExpression = Expression.New(groupByKeyType.GetConstructor(Type.EmptyTypes));
            var groupByKeyInitExpression = Expression.MemberInit(newGroupByKeyExpression, bindings);

            // Create the GroupBy key selector lambda: x => new GroupingKeyType { .... }
            var keySelectorLambda = Expression.Lambda(groupByKeyInitExpression, parameterExpression);

            // --- Step 2: Perform the GroupBy operation ---
            // Call source.GroupBy(keySelectorLambda)
            var groupByMethod = typeof(Queryable).GetMethods()
                .Where(m => m.Name == "GroupBy" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .Select(m => m.MakeGenericMethod(typeof(TSource), groupByKeyType))
                .Single();

            var groupedQuery = (IQueryable) groupByMethod.Invoke(null, new object[] { source, keySelectorLambda });

            // --- Step 3: Create the Dynamic Result Type and Select Projection ---
            // Define properties for the result type: grouping key properties + "Aggregation" property
            var resultProperties = new List<(string Name, Type Type)>();
            resultProperties.AddRange(groupByProperties); // Add all grouping key properties
            resultProperties.Add((aggregationResultFieldName, typeof(int))); // Add the Aggregation property

            var resultType = typeof(TSource);

            // Parameter for select lambda: IGrouping<GroupingKeyType, TSource>
            var groupParameter = Expression.Parameter(typeof(IGrouping<,>).MakeGenericType(groupByKeyType, sourceType), "g");

            // Access the Key of the group
            var groupKeyExpression = Expression.Property(groupParameter, "Key");

            // Build the Aggregation Expression
            Expression aggregationExpression;
            var aggregationFieldProp = sourceType.GetProperty(aggregationResultFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (aggregationFieldProp == null)
                throw new ArgumentException($"Aggregation Field '{aggregationFieldName}' not found on type '{sourceType.Name}'.");

            var selectMethod = typeof(Enumerable).GetMethods()
                .Where(m => m.Name == "Select" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .Single(m => m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                .MakeGenericMethod(sourceType, aggregationFieldProp.PropertyType);

            var elementParameter = Expression.Parameter(sourceType, "elem");
            var elementPropertyAccess = Expression.Property(elementParameter, aggregationFieldProp);
            var elementSelectorLambda = Expression.Lambda(elementPropertyAccess, elementParameter);

            var selectCall = Expression.Call(selectMethod, groupParameter, elementSelectorLambda);

            MethodCallExpression finalCall = selectCall;

            MethodInfo? aggregationMethod = null;

            switch(aggregationOperation)
            {
                case AggregationOperationType.COUNTDISTINCT:
                    var distinctMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Distinct" && m.IsGenericMethodDefinition)
                        .Single(m => m.GetParameters().Length== 1 
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .MakeGenericMethod(aggregationFieldProp.PropertyType);

                    finalCall = Expression.Call(distinctMethod, selectCall);

                    aggregationMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Count" && m.GetParameters().Length == 1)
                        .Single()
                        .MakeGenericMethod (aggregationFieldProp.PropertyType);
                    break;

                case AggregationOperationType.COUNT:
                    aggregationMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Count" && m.GetParameters().Length == 1)
                        .Single()
                        .MakeGenericMethod(aggregationFieldProp.PropertyType);
                    break;
                case AggregationOperationType.SUM:
                    aggregationMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Sum" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsGenericType
                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .Single(m => m.GetParameters()[0].ParameterType.GetGenericArguments()[0] == aggregationFieldProp.PropertyType);
                    break;
                case AggregationOperationType.MAX:
                    aggregationMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Max" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsGenericType
                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .Single(m => m.GetParameters()[0].ParameterType.GetGenericArguments()[0] == aggregationFieldProp.PropertyType);
                    break;
                case AggregationOperationType.MIN:
                    aggregationMethod = typeof(Enumerable).GetMethods()
                        .Where(m => m.Name == "Min" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsGenericType
                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .Single(m => m.GetParameters()[0].ParameterType.GetGenericArguments()[0] == aggregationFieldProp.PropertyType);
                    break;
                default:
                    break;
            }
                
            aggregationExpression = Expression.Call(aggregationMethod, finalCall);


            var newResultTypeExpression = Expression.New(resultType.GetConstructor(Type.EmptyTypes));

            List<MemberBinding> resultMemberBindings = new List<MemberBinding>();

            // Bind grouping key properties from g.Key to the result type's fields
            foreach(var propInfo in groupByProperties)
            {
                var resultField = typeof(TSource).GetProperty(propInfo.Name);
                var groupKeyField = groupByKeyType.GetField(propInfo.Name); // Get the field from the dynamically generated grouping key type.

                // Ensure groupKeyField is not null before using it
                if (groupKeyField == null)
                    throw new InvalidOperationException($"Field '{propInfo.Name}' not found on dynamic grouping key type '{groupByKeyType.Name}'.");

                // Access the value: g.Key.Field
                var propertyAccess = Expression.Field(groupKeyExpression, groupKeyField);
                resultMemberBindings.Add(Expression.Bind(resultField, propertyAccess));
            }

            // Bind the Aggregation property.
            var aggregationResultProp = resultType.GetProperty(aggregationFieldName);
            resultMemberBindings.Add(Expression.Bind(aggregationResultProp, aggregationExpression));

            // Bind grouping key properties to the result type

            var resultInitExpression = Expression.MemberInit(newResultTypeExpression, resultMemberBindings);

            // Create the Select lambda: g => new GroupedResultType { ... }
            var resultSelectorLambda = Expression.Lambda(resultInitExpression, groupParameter);

            // Correct the paramter type of the resultSelectorLambda to match groupedQuery's element type.
            var correctedResultSelectorType = typeof(Func<,>).MakeGenericType(groupedQuery.ElementType, typeof(TSource));
            var correctedResultSelector = Expression.Lambda(resultInitExpression, groupParameter);


            // Call Queryable.Select(groupedQuery, resultSelectorLambda)

            var selectFinalMethod = typeof(Queryable).GetMethods()
                .Where(m => m.Name == "Select" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .Select(m => m.MakeGenericMethod(groupedQuery.ElementType, typeof(TSource)))
                .FirstOrDefault();

            var finalQuery = (IQueryable<TSource>) selectFinalMethod.Invoke(null, new object[] { groupedQuery, correctedResultSelector });

            return finalQuery;
        }
    }
}
