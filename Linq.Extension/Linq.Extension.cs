using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Linq.Extension.Filter;
using Linq.Extension.Pagination;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Linq.Extension.Unique;
using System.ComponentModel.DataAnnotations.Schema;
#if NET6_0_OR_GREATER
using Microsoft.EntityFrameworkCore;
#elif NET48_OR_GREATER
using System.Data.Entity;
using Linq.Extension.CustomExtensionMethods;
#endif
using Linq.Extension.Grouping;

namespace Linq.Extension
{
    public static class LinqDynamicExtension
    {
        private struct FieldNameExpression 
        {
            
        }

        #region GroupByOperationOn

        private static SearchInput CreateGroupSearch(List<GroupKeyNameValue> groupKeyNameValues)
        {
            List<FilterInput> filters = new List<FilterInput>();
            foreach(var keyNameValue in groupKeyNameValues)
            {
                filters.Add(new FilterInput
                {
                    Logic = FilterLogicEnum.and,
                    Operation = FilterOperationEnum.eq,
                    FieldName = keyNameValue.KeyName,
                    Value = keyNameValue.KeyValue
                });
            }

            SearchInput search = new SearchInput()
            {
                FilterGroups = new List<FilterGroupInput>
                {
                    new FilterGroupInput
                    {
                        Logic = FilterLogicEnum.and,
                        Filters = filters
                    }
                }
            };

            return search;
        }

        public static List<GroupValuePair> GetListOfGroupValuePair<T>(this IQueryable<T> source, DbSet<T> entity,
            IDictionary<string, object> parameters) where T: class
        {
            var groupByOperationOn = GetGroupByOperationOn(parameters);

            DistinctByInput distinctBy = new DistinctByInput()
            {
                FieldNames = groupByOperationOn.GroupByFieldNames,
                Pagination = groupByOperationOn.Pagination,
                Search = groupByOperationOn.Search,
                DelimiterFieldValues = groupByOperationOn.DelimiterFieldValues
            };

            var keys = entity
                .GetEFCoreListDataFromDistinctBy(distinctBy);

            List<string> lst = new List<string>();
            lst.AddRange(groupByOperationOn.GroupByFieldNames);
            lst.Add(groupByOperationOn.OperationOnFieldName);

            var data = source
                .AsNoTracking()
                .Where(keys, groupByOperationOn.GroupByFieldNames, groupByOperationOn.DelimiterFieldValues)
                .Where(parameters)
                .DistinctBySelect(lst)
                .ToList();


            List<GroupValuePair> listOfKeyValue = new List<GroupValuePair>();

            foreach(var key in keys)
            {
                List<GroupKeyNameValue> groupKeyNameValues = new List<GroupKeyNameValue>();
                foreach (var field in groupByOperationOn.GroupByFieldNames)
                {
                    groupKeyNameValues.Add(new GroupKeyNameValue 
                    {
                        KeyName = field,
                        KeyValue = Convert.ToString(GetPropertValue<T, object>(key, field))
                    });
                }

                listOfKeyValue.Add(new GroupValuePair
                {
                    Keys = groupKeyNameValues,
                    Value = data
                            .GetGroupByOperationResult(CreateGroupSearch(groupKeyNameValues),
                                groupByOperationFieldName: groupByOperationOn.OperationOnFieldName,
                                groupByOperation: groupByOperationOn.Operation)
                });
            }

            return listOfKeyValue;
        }

        public static IEnumerable<T> Where<T>(this IEnumerable<T> source, SearchInput search)
        {
            var selector = DynamicWherePredicate<T>(search);

            if (selector != null)
                return source.Where(selector.Compile());
            else
                return source;
        }

        public static int GetGroupByOperationResult<T>(this IEnumerable<T> source, SearchInput search,
            string groupByOperationFieldName, GroupByOperationEnum groupByOperation = GroupByOperationEnum.count)
        {
            int result = 0;
            switch (groupByOperation)
            {
                case GroupByOperationEnum.sum:
                    result = source.Where(search)
                        .Sum(GroupByOperationSelectGenerator<T>(groupByOperationFieldName).Compile());
                    break;
                case GroupByOperationEnum.min:
                    result = source.Where(search)
                        .Min(GroupByOperationSelectGenerator<T>(groupByOperationFieldName).Compile());
                    break;
                case GroupByOperationEnum.max:
                    result = source.Where(search)
                        .Max(GroupByOperationSelectGenerator<T>(groupByOperationFieldName).Compile());
                    break;
                default:
                    result = source.Where(search).Count();
                    break;
            }
            return result;
        }

        public static Expression<Func<T, int>> GroupByOperationSelectGenerator<T>(string fieldName)
        {
            var sourceProperties = GetSelectionSetAsDictionaryOfProperties<T>(DelimitedStringToList(fieldName));

            var colName = ToTitleCase(fieldName);
            var property = sourceProperties[colName];

            ParameterExpression sourceItem = Expression.Parameter(typeof(T), "t");

            if (property == null)
            {
                throw new ArgumentNullException($"Property '{colName}' not found on type {typeof(T)}.");
            }

            var propertExpression = Expression.Property(sourceItem, property);
            return Expression.Lambda<Func<T, int>>(propertExpression, sourceItem);
        }

        public static GroupByOperationEnum GetGroupByOperation(IDictionary<string, object> parameters)
        {
            GroupByOperationEnum groupBy = GroupByOperationEnum.count;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Grouping.GroupByOperationEnum"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Grouping.GroupByOperationEnum")
                        || key == "operation"
                        || parameters[key] is GroupByOperationEnum))
                    {
                        groupBy = JsonConvert.DeserializeObject<GroupByOperationEnum>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return groupBy;
        }

        public static GroupByOperationOnInput GetGroupByOperationOn(IDictionary<string, object> parameters)
        {
            GroupByOperationOnInput groupBy = null;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Grouping.GroupByOperationOnInput"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Grouping.GroupByOperationOnInput")
                        || key == "groupByOperationOn"
                        || parameters[key] is GroupByOperationOnInput))
                    {
                        groupBy = JsonConvert.DeserializeObject<GroupByOperationOnInput>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return groupBy;
        }

        #endregion

        #region GroupBy

        public static IQueryable<IGrouping<T, T>> GroupBy<T>(this IQueryable<T> source, List<string> fieldNames)
        {
            var sourceProperties = GetSelectionSetAsDictionaryOfProperties<T>(fieldNames);

            ParameterExpression pe = Expression.Parameter(typeof(T), "t");

            IEnumerable<MemberBinding> bindings = typeof(T).GetProperties()
                .Where(x => sourceProperties.ContainsKey(x.Name))
                .Select(p => Expression.Bind(p, Expression.Property(pe, sourceProperties[p.Name]))).OfType<MemberBinding>();

            var newExpression = Expression.MemberInit(
                Expression.New(typeof(T))
                , bindings);

            return source.GroupBy(Expression.Lambda<Func<T, T>>(newExpression, pe));
        }

        public static IQueryable<IGrouping<T, T>> GroupBy<T>(this IQueryable<T> source, string fieldNames, string delimiter = ",")
        {
            var listFieldNames = DelimitedStringToList(fieldNames, delimiter);
            return source.GroupBy(listFieldNames);
        }

        public static IQueryable<IGrouping<T, T>> GroupBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            var groupBy = GetGroupBy(parameters);
            return source.GroupBy(groupBy);
        }

        public static IQueryable<IGrouping<T, T>> GroupBy<T>(this IQueryable<T> source, GroupByInput groupBy)
        {
            return source.Where(groupBy.Search).GroupBy(groupBy.FieldNames);
        }

        public static GroupByInput GetGroupBy(IDictionary<string, object> parameters)
        {
            GroupByInput groupBy = null;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Grouping.GroupByInput"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Grouping.GroupByInput")
                        || key == "groupBy"
                        || parameters[key] is GroupByInput))
                    {
                        groupBy = JsonConvert.DeserializeObject<GroupByInput>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return groupBy;
        }
        public static GroupByInput GetGroupBy(IDictionary<string, object> parameters, string keyName)
        {
            GroupByInput obj = null;

            if (parameters != null && parameters.Count > 0 && parameters.Keys.Contains(keyName) && parameters[keyName] != null)
            {
                obj = JsonConvert.DeserializeObject<GroupByInput>(JsonConvert.SerializeObject(parameters[keyName]));
            }

            return obj;
        }

        #endregion

        #region Where Extension Methods and Dynamic Where Predicate for Lists

        public static Expression CombinedExpression(Expression first, Expression second, bool isAndLogic = true)
        {
            bool isOriginalFirstNull = first == null;
            if (first == null && second != null)
            {
                first = second;
                return first;
            }
           
            if (first != null && second != null)
            {
                if (!isAndLogic)
                {
                    if (first == null)
                        first = Expression.Constant(false);
                    if (second == null)
                        second = Expression.Constant(false);
                    first = BinaryExpression.OrElse(first, second);
                }
                else
                {
                    if (first == null)
                        first = Expression.Constant(true);
                    if (second == null)
                        second = Expression.Constant(true);
                    first = BinaryExpression.AndAlso(first, second);
                }
            }

            return first;
        }

        public static Expression WhereExpressionWithRelationIds<T, E>(IDictionary<string, object> parameters,
           IEnumerable<E> Ids, string IdFieldName, ParameterExpression pe)
        {
            Expression combined = GetWhereExpressionForIds(Ids, IdFieldName, pe);
            var filterExpr = GetDynamicWherePredicate<T>(parameters, pe);
            if (filterExpr != null)
                combined = GetCombinedExpression(combined, filterExpr);
            return combined;
        }

        public static Expression GetWhereExpressionForIds<E>(IEnumerable<E> Ids, string IdFieldName, ParameterExpression pe)
        {
            Expression combined = null;

            MethodInfo method = null;

            if (Ids != null && !string.IsNullOrWhiteSpace(IdFieldName))
            {
                Ids = Ids.ToList();
                method = Ids.GetType().GetMethod("Contains");
                combined = Expression.Call(Expression.Constant(Ids), method, Expression.Property(pe, IdFieldName));
            }

            return combined;
        }
        public static Expression GetWhereExpressionForIds<T, E>(IEnumerable<E> Ids, string IdFieldName)
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");

            return GetWhereExpressionForIds(Ids, IdFieldName, pe);
        }

        public static Expression<Func<T, bool>> DynamicWherePredicate<T, E>(SearchInput searchInput,
            IEnumerable<E> Ids, string IdFieldName)
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");
            Expression combined = null;

            if (searchInput == null && Ids?.Count() < 0 && string.IsNullOrWhiteSpace(IdFieldName)) return null;

            if (searchInput?.FilterGroups?.Count > 0)
            {
                var sourceProperties = GetSourcePropertiesByFilterGroups<T>(searchInput.FilterGroups);
                if (sourceProperties?.Count > 0 && pe != null)
                {
                    combined = GetCombinedExpressionForFilterGroups(FilterGroups: searchInput.FilterGroups,
                        sourceProperties: sourceProperties, pe: pe);
                }
            }

            combined = GetCombinedExpression(combined, GetWhereExpressionForIds(Ids, IdFieldName, pe));

            return Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe });
        }

        public static Expression<Func<T, bool>> WherePredicateWithRelationalIds<T, E>(IDictionary<string, object> parameters
           , IEnumerable<E> Ids, string IdFieldName)
        {
            //the 'IN' parameter for expression ie T=> condition
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");

            //combine them with and 1=1 Like no expression
            Expression combined = WhereExpressionWithRelationIds<T, E>(parameters, Ids, IdFieldName, pe);

            if (combined == null)
            {
                combined = Expression.Constant(true);
            }
            return Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe });
        }

        public static Expression<Func<T, bool>> DynamicWherePredicate<T>(SearchInput searchInput)
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");
            Expression combined = null;

            if (searchInput == null) return null;

            if (searchInput != null)
            {
                if (searchInput?.FilterGroups?.Count > 0)
                {
                    Dictionary<string, PropertyInfo> sourceProperties = GetSourcePropertiesByFilterGroups<T>(searchInput.FilterGroups);
                    if (sourceProperties?.Count > 0 && pe != null)
                    {
                        combined = GetCombinedExpressionForFilterGroups(FilterGroups: searchInput.FilterGroups,
                            sourceProperties: sourceProperties, pe: pe);
                    }
                    else
                        combined = Expression.Constant(true);
                }
            }

            return Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe });
        }

        public static Expression DynamicWhereExpressionForLists<T>(IDictionary<string, string> parameters,
            ParameterExpression pe, string delimiterFieldValues = ",")
        {
            Expression combined = null;
            MethodInfo method = null;
            Expression e1 = null;

            if (parameters?.Count > 0)
            {
                var sourceProperties = parameters.Keys
                    .DistinctBy(x => x)
                    .ToDictionary(x => x,
                        x => typeof(T).GetProperty(ToTitleCase(x)));

                foreach (var listValue in parameters)
                {
                    e1 = null;
                    if (!string.IsNullOrWhiteSpace(listValue.Value))
                    {
                        var inList = GetListForInOperationFromValue(listValue.Value,
                            sourceProperties[listValue.Key], pe, out method, out Expression propertyExpression,
                            delimiterFieldValues: delimiterFieldValues);
                        if (inList != null && method != null)
                        {
                            e1 = Expression.Call(Expression.Constant(inList), method, propertyExpression);
                            if (e1 != null)
                            {
                                if (combined == null)
                                    combined = e1;
                                else
                                    combined = BinaryExpression.AndAlso(combined, e1);
                            }
                        }
                    }
                }
            }

            if (combined == null)
                combined = Expression.Constant(true);

            return combined;
        }

        public static Expression<Func<T, bool>> DynamicWherePredicateForLists<T>(IDictionary<string, string> parameters
            , string delimiterFieldValues = ",")
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");                  

            return Expression.Lambda<Func<T, bool>>(DynamicWhereExpressionForLists<T>(parameters, pe,
                delimiterFieldValues: delimiterFieldValues), new ParameterExpression[] { pe });
        }              

        public static Expression GetExpressionOfEFCoreListDataFromDistinctBy<T>(DbSet<T> source, DistinctByInput distinctBy
            , ParameterExpression pe, char delimiter =',') where T: class
        {
            Expression combined = null;
            if (source == null || distinctBy == null) return null;

            var list = source.GetEFCoreListDataFromDistinctBy(distinctBy);

            IDictionary<string, string> parameters = GetDictionaryOfFieldAndDelimitedStringValues(list, distinctBy?.FieldNames,
                delimiterFieldValues: distinctBy.DelimiterFieldValues);

            combined = DynamicWhereExpressionForLists<T>(parameters, pe, distinctBy.DelimiterFieldValues);

            return combined;
        }

        public static List<T> GetEFCoreListDataFromDistinctBy<T>(this DbSet<T> source,
            DistinctByInput distinctBy) where T: class
        {
            if (source == null || distinctBy == null) return null;
            var distinctByData = source
                .AsNoTracking()
                .Where(distinctBy?.Search)
                .DistinctBySelect(distinctBy?.FieldNames)
                .Pagination(distinctBy?.Pagination)
                .ToList();

            return distinctByData;
        }

        public static Expression GetExpressionOfEFCoreListDataFromDistinctBy<T, E>(DbSet<T> source, DistinctByInput distinctBy
           , ParameterExpression pe, IEnumerable<E> Ids, string IdFieldName) where T : class
        {
            Expression combined = null;
            if (source == null || distinctBy == null) return null;

            var list = source.GetEFCoreListDataFromDistinctBy(distinctBy, Ids, IdFieldName);

            IDictionary<string, string> parameters = GetDictionaryOfFieldAndDelimitedStringValues(list, distinctBy?.FieldNames,
                delimiterFieldValues: distinctBy.DelimiterFieldValues);

            combined = DynamicWhereExpressionForLists<T>(parameters, pe);

            return combined;
        }

        public static List<T> GetEFCoreListDataFromDistinctBy<T, E>(this DbSet<T> source,
            DistinctByInput distinctBy, IEnumerable<E> Ids, string IdFieldName) where T : class
        {
            if (source == null || distinctBy == null) return null;
            var distinctByData = source
                .AsNoTracking()
                .Where(distinctBy?.Search, Ids, IdFieldName)
                .DistinctBySelect(distinctBy?.FieldNames)
                .Pagination(distinctBy?.Pagination)
                .ToList();

            return distinctByData;
        }

        public static IQueryable<T> WhereWithDistinctBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters,
           DbSet<T> entity) where T : class
        {
            return source.WhereWithDistinctBy(parameters, entity, GetDistinctBy(parameters));
        }

        public static IQueryable<T> WhereWithDistinctBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters,
            DbSet<T> entity, DistinctByInput distinctBy) where T: class
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");
            Expression combined = GetExpressionOfEFCoreListDataFromDistinctBy(entity, distinctBy, pe);
            Expression e1 = GetDynamicWherePredicate<T>(parameters, pe);

            combined = GetCombinedExpression(combined, e1);

            if (combined != null)
                return source.Where(Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe }));
            else
                return source;
        }

        public static IQueryable<T> WhereWithDistinctBy<T, E>(this IQueryable<T> source, IDictionary<string, object> parameters,
          DbSet<T> entity, IEnumerable<E> Ids, string IdFieldName) where T : class
        {
            return source.WhereWithDistinctBy(parameters, entity, GetDistinctBy(parameters), Ids, IdFieldName);           
        }

        public static IQueryable<T> WhereWithDistinctBy<T, E>(this IQueryable<T> source, IDictionary<string, object> parameters,
            DbSet<T> entity, DistinctByInput distinctBy, IEnumerable<E> Ids, string IdFieldName) where T : class
        {
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");
            Expression combined = GetExpressionOfEFCoreListDataFromDistinctBy(entity, distinctBy, pe, Ids, IdFieldName);
            Expression e1 = WhereExpressionWithRelationIds<T, E>(parameters, Ids, IdFieldName, pe);

            combined = GetCombinedExpression(combined, e1);

            if (combined != null)
                return source.Where(Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe }));
            else
                return source;
        }

        public static IQueryable<T> Where<T, E>(this IQueryable<T> source, SearchInput search,
            IEnumerable<E> Ids, string IdFieldName)
        {
            var selector = DynamicWherePredicate<T, E>(search, Ids, IdFieldName);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T>(this IQueryable<T> source, IDictionary<string, string> parameters)
        {
            var selector = DynamicWherePredicateForLists<T>(parameters);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            var selector = DynamicWherePredicate<T>(parameters);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T>(this IQueryable<T> source, SearchInput searchInput)
        {
            var selector = DynamicWherePredicate<T>(searchInput);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T, TDistinctBy>(this IQueryable<T> source, List<TDistinctBy> list,
            string fieldNames, string delimiterFieldNames = ",", string delimiterFieldValues = ",")
        {
            IDictionary<string, string> parameters = GetDictionaryOfFieldAndDelimitedStringValues(list, fieldNames,
                delimiterFieldNames: delimiterFieldNames, delimiterFieldValues: delimiterFieldValues);
            var selector = DynamicWherePredicateForLists<T>(parameters, delimiterFieldValues: delimiterFieldValues);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T, TDistinctBy>(this IQueryable<T> source, List<TDistinctBy> list,
            List<string> fieldNames, string delimiterFieldValues = ",")
        {
            IDictionary<string, string> parameters = GetDictionaryOfFieldAndDelimitedStringValues(list, fieldNames,
                delimiterFieldValues: delimiterFieldValues);
            var selector = DynamicWherePredicateForLists<T>(parameters, delimiterFieldValues: delimiterFieldValues);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }

        public static IQueryable<T> Where<T, E>(this IQueryable<T> source, IDictionary<string, object> parameters
            , IEnumerable<E> Ids, string IdFieldName)
        {
            var selector = WherePredicateWithRelationalIds<T, E>(parameters, Ids, IdFieldName);

            if (selector != null)
                return source.Where(selector);
            else
                return source;
        }
       
        public static SearchInput GetSearch(IDictionary<string, object> parameters)
        {
            SearchInput obj = null;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Filter.SearchInput"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Filter.SearchInput")
                        || key.ToLower() == "search"
                        || parameters[key] is SearchInput))
                    {
                        obj = JsonConvert.DeserializeObject<SearchInput>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return obj;
        }

        public static SearchInput GetSearch(IDictionary<string, object> parameters, string keyName)
        {
            SearchInput obj = null;

            if (parameters != null && parameters.Count > 0 && parameters.Keys.Contains(keyName) && parameters[keyName] != null)
            {
                obj = JsonConvert.DeserializeObject<SearchInput>(JsonConvert.SerializeObject(parameters[keyName]));
            }

            return obj;
        }

        #endregion

        #region DistinctBy Select Extension Methods.
        public static IQueryable<T> DistinctBySelect<T>(this IQueryable<T> source, IEnumerable<string> fieldsNames)
        {
            var selector = DynamicSelectGenerator<T>(fieldsNames);

            if (selector != null)
                return source.Select(selector).Distinct();
            else
                return source.Select(x => x).Distinct();
        }

        public static IQueryable<T> DistinctBySelect<T>(this IQueryable<T> source, string fieldsNames,
            string delimiterFieldNames = ",")
        {
            var selector = DynamicSelectGenerator<T>(fieldsNames, delimiter: delimiterFieldNames);

            if (selector != null)
                return source.Select(selector).Distinct();
            else
                return source.Select(x => x).Distinct();
        }

        public static IQueryable<T> DistinctBySelect<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            var distinctBy = GetDistinctBy(parameters);
            return source.DistinctBySelect(distinctBy);
        }

        public static IQueryable<T> DistinctBySelect<T>(this IQueryable<T> source, DistinctByInput distinctBy)
        {
            if (distinctBy != null)
            {
                var selector = DynamicSelectGenerator<T>(distinctBy.FieldNames);

                if (selector != null)
                    return source.Select(selector).Distinct();
            }
            return source.Select(x => x).Distinct();
        }

        public static IQueryable<T> DistinctBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            return source.DistinctBy(GetDistinctBy(parameters));
        }

        public static IQueryable<T> DistinctBy<T>(this IQueryable<T> source, DistinctByInput distinctBy)
        {
            if (distinctBy != null)
            {
                var selector = DynamicSelectGenerator<T>(distinctBy.FieldNames);

                if (selector != null)
                    return source.Where(distinctBy.Search).Select(selector).Distinct().Pagination(distinctBy.Pagination);
            }
            return source.Select(x => x).Distinct();
        }

        public static IQueryable<T> DistinctBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters,
            bool withPagination, bool withWhere = true)
        {
            return source.DistinctBy(GetDistinctBy(parameters), withPagination: withPagination, withWhere: withWhere);
        }

        public static IQueryable<T> DistinctBy<T>(this IQueryable<T> source, DistinctByInput distinctBy,
            bool withPagination, bool withWhere = true)
        {
            if (distinctBy != null)
            {
                var selector = DynamicSelectGenerator<T>(distinctBy.FieldNames);

                if (selector != null)
                {
                    if (withWhere)
                        source = source.Where(distinctBy.Search);

                    source = source.Select(selector).Distinct();

                    if (withPagination)
                        source = source.Pagination(distinctBy.Pagination);
                    return source;
                }
            }
            return source.Select(x => x).Distinct();
        }

        public static DistinctByInput GetDistinctBy(IDictionary<string, object> parameters)
        {
            DistinctByInput distinctBy = null;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Unique.DistinctByInput"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Unique.DistinctByInput")
                        || key == "distinctBy"
                        || parameters[key] is DistinctByInput))
                    {
                        distinctBy = JsonConvert.DeserializeObject<DistinctByInput>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return distinctBy;
        }
        public static DistinctByInput GetDistinctBy(IDictionary<string, object> parameters, string keyName)
        {
            DistinctByInput distinctBy = null;

            if (parameters != null && parameters.Count > 0 && parameters.Keys.Contains(keyName) && parameters[keyName] != null)
            {
                distinctBy = JsonConvert.DeserializeObject<DistinctByInput>(JsonConvert.SerializeObject(parameters[keyName]));
            }

            return distinctBy;
        }
        #endregion

        #region Pagination, Sort, Take and Distinct Methods

        public static IQueryable<T> Pagination<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            source = source.DistinctIf(parameters);

            PaginationInput pagingState = GetPagination(parameters);
            
            if (pagingState == null)
                return source;
            else
            {
                return source
                    .SortBy(pagingState.Sorts)
                    .SkipIfPositiveNumber(pagingState.Skip)
                    .TakeIfPositiveNumber(pagingState.Take);
            }
        }
        public static IQueryable<T> Pagination<T>(this IQueryable<T> source, IDictionary<string, object> parameters,
            bool distinct = false)
        {
            source = source.DistinctIf(distinct);

            PaginationInput pagingState = GetPagination(parameters);
            if (pagingState == null)
                return source;
            else
            {
                return source
                    .SortBy(pagingState.Sorts)
                    .SkipIfPositiveNumber(pagingState.Skip)
                    .TakeIfPositiveNumber(pagingState.Take);
            }
        }
        public static IQueryable<T> Pagination<T>(this IQueryable<T> source, PaginationInput pagination,
            bool distinct = false)
        {
            source = source.DistinctIf(distinct);
            
            if (pagination == null)
                return source;
            else
            {
                return source
                    .SortBy(pagination.Sorts)
                    .SkipIfPositiveNumber(pagination.Skip)
                    .TakeIfPositiveNumber(pagination.Take);
            }
        }
        public static IQueryable<T> DistinctIf<T> (this IQueryable<T> source, bool distinct = false)
        {
            if (distinct)
                source = source.Distinct();
            return source;
        }
        public static IQueryable<T> DistinctIf<T>(this IQueryable<T> source, IDictionary<string, object> parameters,
            string distinctKeyName = "distinct")
        {
            bool distinct = parameters.Keys.Contains(distinctKeyName) ? (bool)parameters[distinctKeyName] : false;
            if (distinct)
                source = source.Distinct();
            return source;
        }
        public static IQueryable<T> SortBy<T>(this IQueryable<T> source, List<SortInput> sorts)
        {
            try
            {
                if (sorts != null && sorts.Count > 0)
                {
                    MethodInfo method;
                    SortInput sort = null;
                    string prefix = "Order";
                    Expression exprNext = null;
                    PropertyInfo property = null;
                    Type propertyType = null;
                    MethodInfo methodSortExpr = null;
                    for (int i = 0; i < sorts.Count; i++)
                    {
                        prefix = i < 1 ? "Order" : "Then";
                        sort = sorts[i];
                        property = typeof(T).GetProperty(ToTitleCase(sort.FieldName));

                        if (property != null)
                        {
                            //if (property.PropertyType.IsGenericType)
                            //    propertyType = Nullable.GetUnderlyingType(property.PropertyType);
                            //else
                            propertyType = property.PropertyType;
                            if (!string.IsNullOrEmpty(sort.FieldName))
                            {

                                switch (sort.Direction)
                                {
                                    case SortDirectionEnum.desc:
                                        method = typeof(Queryable).GetMethods()
                                        .Where(x => x.Name == prefix + "ByDescending")
                                        .First().MakeGenericMethod(typeof(T), propertyType);
                                        break;
                                    default:
                                        method = typeof(Queryable).GetMethods()
                                        .Where(x => x.Name == prefix + "By")
                                        .First().MakeGenericMethod(typeof(T), propertyType);
                                        break;
                                }
                                if (i == 0)
                                {
                                    exprNext = source.Expression;
                                }
                                methodSortExpr = typeof(LinqDynamicExtension)
                                    .GetMethod("GetSortExpression", BindingFlags.NonPublic | BindingFlags.Static)
                                    .MakeGenericMethod(typeof(T), propertyType);
                                exprNext = Expression.Call(
                                            null,
                                            method,
                                            exprNext,
                                            (Expression)methodSortExpr.Invoke(null, new string[] { sort.FieldName }));
                            }
                        }
                    }
                    return source.Provider.CreateQuery<T>(exprNext);
                }
                else
                    return source;
            }
            catch (Exception Ex)
            {
                return source;
            }
        }
        public static IQueryable<T> SortBy<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            try
            {
                PaginationInput pagingState = GetPagination(parameters);
                
                if (pagingState != null)
                {
                    return source.SortBy(pagingState.Sorts);
                }
                else
                    return source;
            }
            catch (Exception Ex)
            {
                return source;
            }
        }
        public static IQueryable<T> TakeIfPositiveNumber<T>(this IQueryable<T> source, int? count)
        {
            if (count.HasValue && count.Value > 0)
            {
                var method = typeof(Queryable).GetMethods()
                    .Where(x => x.Name == "Take")
                    .First().MakeGenericMethod(typeof(T));

                return source.Provider.CreateQuery<T>(
                Expression.Call(
                    null,
                    method,
                    source.Expression,
                    Expression.Constant(count))
                    );
            }
            else
                return source;
        }
        public static IQueryable<T> Take<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            PaginationInput pagingState = GetPagination(parameters);
            if (pagingState != null)
            {
                return source.TakeIfPositiveNumber(pagingState.Take);
            }
            else
                return source;
        }
        public static IQueryable<T> SkipIfPositiveNumber<T>(this IQueryable<T> source, int? count)
        {
            if (count.HasValue && count.Value > 0)
            {
                var method = typeof(Queryable).GetMethods()
                    .Where(x => x.Name == "Skip")
                    .First().MakeGenericMethod(typeof(T));

                return source.Provider.CreateQuery<T>(
                Expression.Call(
                    null,
                    method,
                    source.Expression,
                    Expression.Constant(count))
                    );
            }
            else
                return source;
        }
        public static IQueryable<T> Skip<T>(this IQueryable<T> source, IDictionary<string, object> parameters)
        {
            PaginationInput pagingState = GetPagination(parameters);
            if (pagingState != null)
            {
                return source.SkipIfPositiveNumber(pagingState.Skip);
            }
            else
                return source;
        }
        public static PaginationInput GetPagination(IDictionary<string, object> parameters)
        {
            PaginationInput pagingState = null;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var key in parameters.Keys)
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extensions.Base.Pagination.PaginationInput"
                        || (bool)parameters[key]?.GetType()?.FullName.Contains("Pagination.PaginationInput")
                        || key.ToLower() == "pagination"
                        || parameters[key] is PaginationInput))
                    {
                        pagingState = JsonConvert.DeserializeObject<PaginationInput>(JsonConvert.SerializeObject(parameters[key]));
                        break;
                    }
            }

            return pagingState;
        }

        public static PaginationInput GetPagination(IDictionary<string, object> parameters, string keyName)
        {
            PaginationInput obj = null;

            if (parameters != null && parameters.Count > 0 && parameters.Keys.Contains(keyName) && parameters[keyName] != null)
            {
                obj = JsonConvert.DeserializeObject<PaginationInput>(JsonConvert.SerializeObject(parameters[keyName]));
            }

            return obj;
        }

        #endregion

        #region Selection Extension Methods for Actual Type instead of Dynamic Type.

        public static IQueryable<dynamic> SelectAsAnomouysType<T>(this IQueryable<T> source, IEnumerable<string> fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            var selector = DynamicSelectGeneratorAnomouysType<T>(fieldsNames, fetchParentEntityAlongWithParentlId);

            if (selector != null)
                return source.Select(selector);
            else
                return (IQueryable<dynamic>)source;
        }

        public static IQueryable<T> Select<T>(this IQueryable<T> source, IEnumerable<string> fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            var selector = DynamicSelectGenerator<T>(fieldsNames, fetchParentEntityAlongWithParentlId);

            if (selector != null)
                return source.Select(selector);
            else
                return source.Select(x => x);
        }

        public static IQueryable<T> Select<T>(this IQueryable<T> source, string fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            var selector = DynamicSelectGenerator<T>(fieldsNames, fetchParentEntityAlongWithParentlId);

            if (selector != null)
                return source.Select(selector);
            else
                return source.Select(x => x);
        }

        public static Expression<Func<T, T>> DynamicSelectGenerator<T>(IEnumerable<string> fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            if (fieldsNames == null || fieldsNames?.Count() < 1) return null;
            var sourceProperties = GetSelectionSetAsDictionaryOfProperties<T>(fieldsNames, fetchParentEntityAlongWithParentlId);

            ParameterExpression sourceItem = Expression.Parameter(typeof(T), "t");

            IEnumerable<MemberBinding> bindings = typeof(T).GetProperties()
                .Where(x => sourceProperties.ContainsKey(x.Name))
                .Select(p => Expression.Bind(p, Expression.Property(sourceItem, sourceProperties[p.Name]))).OfType<MemberBinding>();

            var selector = Expression.Lambda<Func<T, T>>(
                Expression.MemberInit(
                    Expression.New(typeof(T))
                    , bindings)
                , sourceItem);

            return selector;
        }
        public static Expression<Func<T, T>> DynamicSelectGenerator<T>(string fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false, string delimiter = ",")
        {
            var listFieldNames = DelimitedStringToList(fieldsNames, delimiter: delimiter);
            return DynamicSelectGenerator<T>(listFieldNames, fetchParentEntityAlongWithParentlId);
        }

        public static List<string> DelimitedStringToList(string delimitedString, string delimiter = ",")
        {
            if (string.IsNullOrWhiteSpace(delimitedString)) return null;

            return delimitedString.Split(!string.IsNullOrWhiteSpace(delimiter) ? Convert.ToChar(delimiter) : ',').Select(x => x.Trim()).ToList();
        }

        public static IDictionary<string, string> GetDictionaryOfFieldAndDelimitedStringValues<T>
            (List<T> list, List<string> fieldsNames,  string delimiterFieldValues = ",")
        {
            Dictionary<string, string> dicFieldDelimitedValue = new Dictionary<string, string>();

            if (list?.Count > 0 && fieldsNames?.Count > 0)
            {
                foreach (var field in fieldsNames)
                {
                    dicFieldDelimitedValue.Add(field, ConvertListOfValuesToDelimitedString(list, field,
                        delimiterFieldValues: delimiterFieldValues));
                }
            }
            return dicFieldDelimitedValue;
        }
        public static IDictionary<string, string> GetDictionaryOfFieldAndDelimitedStringValues<T>
            (List<T> list, string fieldsNames, string delimiterFieldNames = ",", string delimiterFieldValues = ",")
        {
            Dictionary<string, string> dicFieldDelimitedValue = new Dictionary<string, string>();
            var lstFieldName = DelimitedStringToList(fieldsNames, delimiter: delimiterFieldNames);

            if (list?.Count > 0 && lstFieldName?.Count > 0)
            {
                foreach (var field in lstFieldName)
                {
                    dicFieldDelimitedValue.Add(field, ConvertListOfValuesToDelimitedString(list, field,
                        delimiterFieldValues: delimiterFieldValues));
                }
            }
            return dicFieldDelimitedValue;
        }

        public static string ConvertListOfValuesToDelimitedString<T>(List<T> list, string columnName,
            string delimiterFieldValues = ",")
        {
            delimiterFieldValues = string.IsNullOrWhiteSpace(delimiterFieldValues) ? "," : delimiterFieldValues;

            string colName = ToTitleCase(columnName);
            var propertyInfo = typeof(T).GetProperty(colName);
            if (propertyInfo == null)
            {
                throw new ArgumentNullException($"Property '{colName}' not found on type {typeof(T)}.");
            }

            Type t = propertyInfo.PropertyType.GetUnderlyingType();

            string delimitedString = string.Empty;

            if (t.FullName.ToLower().Contains("int64"))
            {
                if(propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues, 
                        list.DistinctBy<T, Nullable<Int64>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<Int64>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, Int64>(colName)
                        .Select(x => GetPropertValue<T, Int64>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("double"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<double>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<double>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, double>(colName)
                        .Select(x => GetPropertValue<T, double>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("float"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<float>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<float>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, float>(colName)
                        .Select(x => GetPropertValue<T, float>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("int32"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<Int32>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<Int32>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, Int32>(colName)
                        .Select(x => GetPropertValue<T, Int32>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("int16"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<Int16>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<Int16>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, Int16>(colName)
                        .Select(x => GetPropertValue<T, Int16>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("int"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<int>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<int>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, int>(colName)
                        .Select(x => GetPropertValue<T, int>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("bool"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<bool>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<bool>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, bool>(colName)
                        .Select(x => GetPropertValue<T, bool>(x, colName).ToString())
                        .ToArray());
            }
            else if (t.FullName.ToLower().Contains("string"))
            {
                delimitedString = string.Join(delimiterFieldValues,
                     list.DistinctBy<T, string>(colName)
                    .Select(x => GetPropertValue<T, string>(x, colName)?.ToString())
                    .ToArray());
            }
            else if (t.FullName.ToLower().Contains("date"))
            {
                if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                    delimitedString = string.Join(delimiterFieldValues,
                        list.DistinctBy<T, Nullable<DateTime>>(colName)
                        .Select(x => GetPropertValue<T, Nullable<DateTime>>(x, colName)?.ToString())
                        .ToArray());
                else
                    delimitedString = string.Join(delimiterFieldValues,
                         list.DistinctBy<T, DateTime>(colName)
                        .Select(x => GetPropertValue<T, DateTime>(x, colName).ToString())
                        .ToArray());
            }

            return delimitedString;
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, string columnName)
        {
            var parameterExpression = Expression.Parameter(typeof(TSource), "x");

            var propertyInfo = typeof(TSource).GetProperty(columnName);
            if (propertyInfo == null)
            {
                throw new ArgumentNullException($"Property '{columnName}' not found on type {typeof(TSource)}.");
            }

            Expression<Func<TSource, TKey>> keySelectorExpression = Expression.Lambda<Func<TSource, TKey>>(
                Expression.Property(parameterExpression, columnName)
                , parameterExpression);

            return source.DistinctBy(keySelectorExpression.Compile());
        }

        public static TValue GetPropertValue<TSource, TValue>(TSource obj, string propertyName)
        {
            string colName = ToTitleCase(propertyName);
            var propertyInfo = typeof(TSource).GetProperty(colName);
            if (propertyInfo == null)
            {
                throw new ArgumentNullException($"Property '{colName}' not found on type {typeof(TSource)}.");
            }

            return (TValue) propertyInfo.GetValue(obj);
        }

        #endregion

        public static Expression<Func<TSource, T>> DynamicSelectGenerator<TSource, T>(string Fields = "")
        {
            string[] EntityFields;
            if (Fields == "")
                // get Properties of the T
                EntityFields = typeof(T).GetProperties().Select(propertyInfo => propertyInfo.Name).ToArray();
            else
                EntityFields = Fields.Split(',');

            // input parameter "o"
            var xParameter = Expression.Parameter(typeof(TSource), "o");

            // new statement "new Data()"
            var xNew = Expression.New(typeof(T));

            // create initializers
            var bindings = EntityFields.Select(o => o.Trim())
                .Select(o =>
                {

                    // property "Field1"
                    var mi = typeof(T).GetProperty(o);

                    // original value "o.Field1"
                    var xOriginal = Expression.Property(xParameter, mi);

                    // set value "Field1 = o.Field1"
                    return Expression.Bind(mi, xOriginal);
                }
            );

            // initialization "new Data { Field1 = o.Field1, Field2 = o.Field2 }"
            var xInit = Expression.MemberInit(xNew, bindings);

            // expression "o => new Data { Field1 = o.Field1, Field2 = o.Field2 }"
            var lambda = Expression.Lambda<Func<TSource, T>>(xInit, xParameter);

            // compile to Func<Data, Data>
            return lambda;
        }

        public static IQueryable SelectDynamic(this IQueryable source, IEnumerable<string> fieldNames)
        {
            Dictionary<string, PropertyInfo> sourceProperties = fieldNames.ToDictionary(name => name, name => source.ElementType.GetProperty(name));
            Type dynamicType = LinqRuntimeTypeBuilder.GetDynamicType(sourceProperties.Values);

            ParameterExpression sourceItem = Expression.Parameter(source.ElementType, "t");
            IEnumerable<MemberBinding> bindings = dynamicType.GetFields().Select(p => Expression.Bind(p, Expression.Property(sourceItem, sourceProperties[p.Name]))).OfType<MemberBinding>();

            Expression selector = Expression.Lambda(Expression.MemberInit(
                Expression.New(dynamicType.GetConstructor(Type.EmptyTypes)), bindings), sourceItem);

            return source.Provider.CreateQuery(Expression.Call(typeof(Queryable), "Select", new Type[] { source.ElementType, dynamicType },
                         Expression.Constant(source), selector));
        }
        private static Type GetUnderlyingType(this Type source)
        {
            if (source.IsGenericType)
                return Nullable.GetUnderlyingType(source);
            else
                return source;
        }
       
        public static Expression GetDynamicWherePredicate<T>(IDictionary<string, object> parameters, ParameterExpression pe)
        {
            //combine them with and 1=1 Like no expression
            Expression combined = null;
            

            if (parameters != null && parameters.Keys != null && parameters.Keys.Count > 0)
            {
                SearchInput searchInput = null;
                Dictionary<string, PropertyInfo> sourceProperties = null;
                Expression columnValue = null;
                Expression columnNameProperty = null;
                Expression e1 = null;
                object value = null;

                foreach (var key in parameters.Keys)
                {
                    //if (key == "search")
                    if (parameters[key] != null
                        && (parameters[key]?.GetType()?.FullName == "GraphQL.Extension.Base.Filter.SearchInput"
                            || key?.ToLower() == "search"
                            || parameters[key] is SearchInput
                        ))
                    {
                        searchInput = JsonConvert.DeserializeObject<SearchInput>(JsonConvert.SerializeObject(parameters[key]));
                        //searchInput = parameters[key].GetPropertyValue<SearchInput>();
                        //context.GetArgument<SearchInput>(key);

                        if (searchInput?.FilterGroups?.Count > 0 )
                        {
                            sourceProperties = GetSourcePropertiesByFilterGroups<T>(searchInput.FilterGroups);
                            if (sourceProperties?.Count > 0 && pe != null)
                            {
                                combined = GetCombinedExpressionForFilterGroups(FilterGroups: searchInput.FilterGroups,
                                    sourceProperties: sourceProperties, pe: pe);
                            } // End of if (searchInput?.FilterGroups?.Count > 0)
                            else
                                combined = null;
                        }
                    }
                    //else if (key != "pagination")
                    else if ( parameters[key] != null
                        && parameters[key]?.GetType()?.FullName != "GraphQL.Extension.Base.Pagination.PaginationInput"
                        && parameters[key] is not PaginationInput)
                    {
                        value = parameters[key];
                        var property = typeof(T).GetProperty(ToTitleCase(key));
                        if (property != null)
                        {
                            columnNameProperty = Expression.Property(pe, property.Name);
                            columnValue = GetConstantColumnValueExpression(value, property.PropertyType);
                            e1 = Expression.Equal(columnNameProperty, columnValue);
                            combined = GetCombinedExpression(combined, e1, null);
                        }
                    }
                    
                }
            }
            return combined;
        }

        private static Expression GetCombinedExpressionForFilterGroups(List<FilterGroupInput> FilterGroups,
             Dictionary<string, PropertyInfo> sourceProperties, ParameterExpression pe)
        {
            Expression combined = null, groupCombined, filterCombined;

            Dictionary<int, Expression> filterGroupExpressions = new Dictionary<int, Expression>();
            //foreach(var filterGroup in searchInput.FilterGroups)
            for (int groupIndex = 0; groupIndex < FilterGroups.Count; groupIndex++)
            {
                var filterGroup = FilterGroups[groupIndex];
                combined = null;
                groupCombined = null;
                filterCombined = null;
                if (filterGroup.ChildGroups?.Count > 0)
                {
                    groupCombined = GetCombinedExpressionForFilterGroups(filterGroup.ChildGroups, sourceProperties, pe);
                }
                if (filterGroup?.Filters?.Count > 0)
                {
                    filterCombined = GetCombinedExpressionForFilters(filterGroup.Filters, sourceProperties, pe);                    
                } // End of if (filterGroup?.Filters?.Count > 0)

                if (groupCombined != null && filterCombined != null)
                    combined = GetGroupCombinedExpression(groupCombined, filterCombined,
                        filterGroup.Filters[0].Logic);
                else if (groupCombined != null && filterCombined == null)
                    combined = groupCombined;
                else if (groupCombined == null && filterCombined != null)
                    combined = filterCombined;
                else
                    combined = null;

                filterGroupExpressions.Add(groupIndex, combined);

            } // End of for (int groupIndex = 0; groupIndex < searchInput.FilterGroups.Count; groupIndex++)
            if (FilterGroups?.Count > 1)
            {
                combined = filterGroupExpressions[0];
                //foreach( int groupIndex in filterGroupExpressions.Keys)
                for (int groupIndex = 1; groupIndex < filterGroupExpressions?.Count; groupIndex++)
                {
                    combined = GetGroupCombinedExpression(combined,
                        filterGroupExpressions[groupIndex], FilterGroups[groupIndex].Logic);
                }
            }

            return combined;
        }

        private static Expression GetCombinedExpressionForFilters(List<FilterInput> Filters,
             Dictionary<string, PropertyInfo> sourceProperties, ParameterExpression pe)
        {
            Expression combined = null;
            MethodInfo method = null;
            Expression columnNameProperty = null;
            Expression e1 = null;
            object value = null;

            var distinctFieldNames = Filters.DistinctBy(f => f.FieldName)
                        .Select(f => f.FieldName)
                        .ToList();
            List<FilterInput> fieldFilters = null;
            if (distinctFieldNames?.Count > 0)
            {
                Dictionary<FilterInput, Expression> fieldExpressions = new Dictionary<FilterInput, Expression>();
                Expression fieldCombined = null;
                foreach (var fieldName in distinctFieldNames)
                {
                    fieldFilters = null;
                    fieldFilters = Filters
                        .FindAll(f => f.FieldName == fieldName)
                        .ToList();

                    if (fieldFilters?.Count > 0)
                    {
                        fieldCombined = null;
                        fieldExpressions.Add(fieldFilters[0], null);
                        foreach (FilterInput filterInput in fieldFilters)
                        {
                            if (filterInput != null)
                            {
                                columnNameProperty = Expression.Property(pe, sourceProperties[filterInput.FieldName].Name);
                                value = filterInput.Value;
                                //columnValue = GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType);
                                switch (filterInput.Operation)
                                {
                                    case FilterOperationEnum.gt:
                                        e1 = Expression.GreaterThan(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.gte:
                                        e1 = Expression.GreaterThanOrEqual(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.lt:
                                        e1 = Expression.LessThan(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.lte:
                                        e1 = Expression.LessThanOrEqual(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.neq:
                                        e1 = Expression.NotEqual(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.contains:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'contains' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("Contains"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.notcontains:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notcontains' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("Contains"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Not(Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType)));
                                        break;
                                    case FilterOperationEnum.startswith:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'startswith operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("StartsWith"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.notstartswith:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notstartswith' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("StartsWith"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Not(Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType)));
                                        break;
                                    case FilterOperationEnum.endswith:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'endswith' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("EndsWith"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                    case FilterOperationEnum.notendswith:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notendswith' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("EndsWith"
                                            , new Type[] { typeof(string) });
                                        e1 = Expression.Not(Expression.Call(columnNameProperty, method,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType)));
                                        break;
                                    case FilterOperationEnum.containsinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'containsinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , false, true, false, false);
                                        break;
                                    case FilterOperationEnum.notcontainsinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notcontainsinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , true, true, false, false);
                                        break;
                                    case FilterOperationEnum.startswithinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'startswithinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , false, false, true, false);
                                        break;
                                    case FilterOperationEnum.notstartswithinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notstartswithinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , true, false, true, false);
                                        break;
                                    case FilterOperationEnum.endswithinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'endswithinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , false, false, false, true);
                                        break;
                                    case FilterOperationEnum.notendswithinlist:
                                        if (!(sourceProperties[filterInput.FieldName].PropertyType.FullName.Contains("String")))
                                            throw new Exception("Only fields of 'String' type can have 'notendswithinlist' operation. FieldName: '"
                                                + filterInput.FieldName + "'");
                                        e1 = GetCombinedStringNonEqualInListExpressions(filterInput, sourceProperties, columnNameProperty
                                            , true, false, false, true);
                                        break;
                                    case FilterOperationEnum.inlist:
                                        string strValue = Convert.ToString(value);
                                        if (!string.IsNullOrEmpty(strValue))
                                        {
                                            var inList = GetListForInOperationFromValue(strValue,
                                                sourceProperties[filterInput.FieldName], pe, out method, out Expression propExpression,
                                                delimiterFieldValues: filterInput.DelimiterListOfValues);
                                            if (inList != null && method != null)
                                                e1 = Expression.Call(Expression.Constant(inList), method, propExpression);
                                        }
                                        break;
                                    case FilterOperationEnum.notinlist:
                                        string strValue2 = Convert.ToString(value);
                                        if (!string.IsNullOrEmpty(strValue2))
                                        {
                                            var inList = GetListForInOperationFromValue(strValue2,
                                                sourceProperties[filterInput.FieldName], pe, out method, out Expression propExpression,
                                                delimiterFieldValues: filterInput.DelimiterListOfValues);
                                            if (inList != null && method != null)
                                                e1 = Expression.Not(
                                                    Expression.Call(Expression.Constant(inList), method, propExpression));
                                        }
                                        break;
                                    default:
                                        e1 = Expression.Equal(columnNameProperty,
                                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                                        break;
                                }
                                fieldCombined = GetCombinedExpression(fieldCombined, e1, filterInput);
                            } // End of if (filterInput != null)
                        } // End of foreach (FilterInput filterInput in fieldFilters)
                        fieldExpressions[fieldExpressions.Keys.Last()] = fieldCombined;
                    } // End of if (fieldFilters?.Count > 0)
                } // End of foreach (var fieldName in distinctFieldNames)
                foreach (FilterInput fieldFilter in fieldExpressions.Keys)
                {
                    combined = GetCombinedExpression(combined, fieldExpressions[fieldFilter], fieldFilter);
                }
            } // End of if (distinctFieldNames?.Count > 0)

            return combined;
        }

        private static Dictionary<string, PropertyInfo> GetSourcePropertiesByFilterGroups<T>
            (List<FilterGroupInput> FilterGroups)
        {
            Dictionary<string, PropertyInfo> sourceProperties = new Dictionary<string, PropertyInfo>()
                , groupProperties, filterProperties;

            foreach (var filterGroup in FilterGroups)
            {
                groupProperties = null;
                filterProperties = null;
                if (filterGroup?.ChildGroups?.Count > 0)
                {
                    groupProperties = GetSourcePropertiesByFilterGroups<T>(filterGroup.ChildGroups);
                    if (groupProperties?.Count > 0)
                    {
                        foreach (var groupProp in groupProperties)
                        {
                            if (!(bool)sourceProperties?.ContainsKey(groupProp.Key))
                            {
                                sourceProperties.Add(groupProp.Key, groupProp.Value);
                            }
                        }
                    }
                }
                if (filterGroup?.Filters?.Count > 0)
                {
                    filterProperties = GetSourcePropertiesByFilters<T>(filterGroup.Filters);
                    if (filterProperties?.Count > 0)
                    {
                        foreach (var filterProp in filterProperties)
                        {
                            if (!(bool)sourceProperties?.ContainsKey(filterProp.Key))
                            {
                                sourceProperties.Add(filterProp.Key, filterProp.Value);
                            }
                        }
                    }
                }
            }

            return sourceProperties;
        }

        private static Dictionary<string, PropertyInfo> GetSourcePropertiesByFilters<T>
            (List<FilterInput> Filters)
        {
            Dictionary<string, PropertyInfo> sourceProperties = new Dictionary<string, PropertyInfo>();

            var sPropertiesInner = Filters
                    .DistinctBy(x => x.FieldName)
                .ToDictionary(filter => filter.FieldName,
                    filter => typeof(T).GetProperty(ToTitleCase(filter.FieldName)));
            if (sPropertiesInner?.Count > 0)
            {
                foreach (var sPropInner in sPropertiesInner)
                {
                    if (sPropInner.Value != null && !(bool)sourceProperties?.ContainsKey(sPropInner.Key))
                        sourceProperties.Add(sPropInner.Key, sPropInner.Value);
                }
            }
            if (sourceProperties == null || sourceProperties.Count == 0)
                throw new Exception
                    ($"Filters can't be empty. Either provide filters or remove 'search' argument");// on type {context.ReturnType.GetType().GenericTypeArguments[0].Name}");
            var nullProps = sourceProperties.Where(x => x.Value == null).FirstOrDefault();
            if (!nullProps.Equals(default(KeyValuePair<string, PropertyInfo>)))
                throw new Exception
                    ($"Invalid Filter: filed name '{nullProps.Key}' doesn't exists ");//in the type {context.ReturnType.GetType().GenericTypeArguments[0].Name}.");

            return sourceProperties;
        }

        private static Expression GetCombinedStringNonEqualInListExpressions(FilterInput filterInput, 
            Dictionary<string, PropertyInfo> sourceProperties,
            Expression columnNameProperty,
            bool isNot, bool isContains, bool isStartsWith, bool isEndsWith)
        {
            Expression fieldCombined = null;
            FilterInput filterInputLogic = new FilterInput
            {
                Logic = isNot ? FilterLogicEnum.and : FilterLogicEnum.or
            };
            MethodInfo method = null;
            Expression e1 = null;
            object value = null;
            FilterInput[] fieldFilters = new FilterInput[] { };
            var values = filterInput.Value.Split(!string.IsNullOrWhiteSpace(filterInput.DelimiterListOfValues) 
                ? Convert.ToChar(filterInput.DelimiterListOfValues) : ',');
            string val = string.Empty;
            foreach(string v in values)
            {
                val = v;
                if (!string.IsNullOrEmpty(val))
                {
                    val = val.Trim();
                    value = val;
                    if (isStartsWith)
                    {
                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("StartsWith"
                                       , new Type[] { typeof(string) });
                        e1 = Expression.Call(columnNameProperty, method,
                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                    }
                    else if (isEndsWith)
                    {
                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("EndsWith"
                                        , new Type[] { typeof(string) });
                        e1 = Expression.Call(columnNameProperty, method,
                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                    }
                    else if (isContains)
                    {
                        method = sourceProperties[filterInput.FieldName].PropertyType.GetMethod("Contains"
                                        , new Type[] { typeof(string) });
                        e1 = Expression.Call(columnNameProperty, method,
                            GetConstantColumnValueExpression(value, sourceProperties[filterInput.FieldName].PropertyType));
                    }
                    if (isNot)
                    {
                        fieldCombined = GetCombinedExpression(fieldCombined, Expression.Not(e1), filterInputLogic);
                    }
                    else
                        fieldCombined = GetCombinedExpression(fieldCombined, e1, filterInputLogic);

                }
            }
           
            return fieldCombined;
        }
        public static Expression<Func<T, bool>> DynamicWherePredicate<T>(IDictionary<string, object> parameters)
        {
            //the 'IN' parameter for expression ie T=> condition
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");
            Expression combined = GetDynamicWherePredicate<T>(parameters, pe);
            if (combined == null)
            {
                combined = Expression.Constant(true);
            }

            //create and return the predicate
            return Expression.Lambda<Func<T, bool>>(combined, new ParameterExpression[] { pe });
        }
        private static object GetListForInOperationFromValue(string value, PropertyInfo propertyInfo, ParameterExpression pe
            , out MethodInfo method
            , out Expression propertyExpression
            , string delimiterFieldValues = ",")
        {
            delimiterFieldValues = !string.IsNullOrWhiteSpace(delimiterFieldValues) ? delimiterFieldValues : ",";
            object l = null;
            method = null;
            Type t = propertyInfo.PropertyType.GetUnderlyingType();

            var genericType = typeof(List<>).MakeGenericType(t);
            var instance = Activator.CreateInstance(genericType);
            method = instance.GetType().GetMethod("Contains");

            if (t.FullName.ToLower().Contains("int64"))
            {
                l = value.Split(Convert.ToChar(delimiterFieldValues)).Select(Int64.Parse).ToList();

            }
            else if (t.FullName.ToLower().Contains("int"))
            {
                l = value.Split(Convert.ToChar(delimiterFieldValues)).Select(int.Parse).ToList();

            }
            else if (t.FullName.ToLower().Contains("bool"))
            {
                l = value.Split(Convert.ToChar(delimiterFieldValues)).Select(bool.Parse).ToList();
            }
            else if (t.FullName.ToLower().Contains("string"))
            {
                l = value.Split(Convert.ToChar(delimiterFieldValues)).ToList();
            }
            else if (t.FullName.ToLower().Contains("date"))
            {
                l = value.Split(Convert.ToChar(delimiterFieldValues)).Select(DateTime.Parse).ToList();
            }

            if (propertyInfo.PropertyType.FullName.ToLower().Contains("nullable"))
                propertyExpression =
                    propertyExpression = Expression.Convert(Expression.Property(pe, propertyInfo.Name), t);
            else
                propertyExpression = Expression.Property(pe, propertyInfo.Name);

            return l;
        }
        private static ConstantExpression GetConstantColumnValueExpression(object value, Type propertyType)
        {
            ConstantExpression columnValue = null;
            if (value != null)
            {
                if (propertyType.FullName.ToLower().Contains("int"))
                {
                    columnValue = Expression.Constant(Convert.ToInt32(value), propertyType);
                }
                else if (propertyType.FullName.ToLower().Contains("bool"))
                {
                    columnValue = Expression.Constant(Convert.ToBoolean(value), propertyType);
                }
                else if (propertyType.FullName.ToLower().Contains("string"))
                {
                    columnValue = Expression.Constant(Convert.ToString(value), propertyType);
                }
                else if (propertyType.FullName.ToLower().Contains("datetime"))
                {
                    columnValue = Expression.Constant(Convert.ToDateTime(value), propertyType);
                }
                else if (propertyType.FullName.ToLower().Contains("date"))
                {
                    columnValue = Expression.Constant(Convert.ToDateTime(value).Date, propertyType);
                }
            }
            else
                columnValue = Expression.Constant(value);
            return columnValue;
        }

        private static Expression GetGroupCombinedExpression(Expression first, Expression second, FilterLogicEnum logic)
        {
            bool isOriginalFirstNull = first == null;
            if (first == null && second != null)
            {
                first = second;
                return first;
            }
            if (first == null)
            {
                first = Expression.Constant(true);
            }
            if (second == null)
                second = Expression.Constant(true);
            if (first != null && second != null)
            {

                if (!isOriginalFirstNull)
                {
                    switch (logic)
                    {
                        case FilterLogicEnum.or:
                            first = BinaryExpression.OrElse(first, second);
                            //first = Expression.Or(first, second);
                            break;
                        default:
                            first = BinaryExpression.AndAlso(first, second);
                            //first = Expression.And(first, second);
                            break;
                    }
                }
                else
                    first = second;// Expression.And(first, second);

            }

            return first;
        }

        private static Expression GetCombinedExpression(Expression first, Expression second, FilterInput filterInput = null)
        {
            bool isOriginalFirstNull = first == null;
            if (first == null && second != null)
            {
                first = second;
                return first;
            }
            
            if (first != null && second != null)
            {
                if (filterInput != null)
                {
                    if (!isOriginalFirstNull)
                    {
                        switch (filterInput.Logic)
                        {
                            case FilterLogicEnum.or:
                                first ??= Expression.Constant(false);
                                second ??= Expression.Constant(false);
                                first = BinaryExpression.OrElse(first, second);
                                //first = Expression.Or(first, second);
                                break;
                            default:
                                first ??= Expression.Constant(true);
                                second ??= Expression.Constant(true);
                                first = BinaryExpression.AndAlso(first, second);
                                //first = Expression.And(first, second);
                                break;
                        }
                    }
                    else
                        first = second;// Expression.And(first, second);
                }
                else
                {
                    first ??= Expression.Constant(true);
                    second ??= Expression.Constant(true);
                    first = BinaryExpression.AndAlso(first, second);
                }
            }

            return first;
        }
        private static PropertyInfo GetScalarPropertyByNameStartsWith(this Type type, string name)
        {
            var properties = type.GetProperties()
                .Where(p => p.Name.StartsWith(name) && p.PropertyType.FullName.Contains("System")
                    && !p.PropertyType.FullName.Contains("Generic")
                );

            if (properties != null && properties.Count() > 0)
                return properties.ToList()[0];
            else
                return null;
        }
        private static List<PropertyInfo> GetScalarPropertyByFullNameOrStartsWith(this Type type, string name,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            Regex exprFullMacth = new Regex("^" + name + "$", RegexOptions.IgnoreCase);
            Regex exprStartsWith = new Regex("^" + name + ".*$", RegexOptions.IgnoreCase);
            var properties = type.GetProperties()
                .Where(p => exprFullMacth.IsMatch(p.Name)
                && p.PropertyType.FullName.Contains("System")
                        && !p.PropertyType.FullName.Contains("Generic")
                );
            if (properties == null || properties.Count() < 1)
            {
                //#if NET6_0_OR_GREATER
                var fkAttribute = type.GetProperties()
                    .Where(p => exprFullMacth.IsMatch(p.Name) && p.GetCustomAttribute<ForeignKeyAttribute>() != null)
                    .Select(f => f.GetCustomAttribute<ForeignKeyAttribute>())
                    .FirstOrDefault();
                if (fkAttribute != null)
                {
                    if (fetchParentEntityAlongWithParentlId)
                        properties = type.GetProperties()
                            .Where(p => !p.PropertyType.FullName.Contains("Generic") &&
                            (exprFullMacth.IsMatch(p.Name) || fkAttribute.Name.ToLower() == p.Name.ToLower()));
                    else
                        properties = type.GetProperties()
                            .Where(p => fkAttribute.Name.ToLower() == p.Name.ToLower());

                }
                else
                //#endif
                {
                    if (fetchParentEntityAlongWithParentlId)
                        properties = type.GetProperties()
                            .Where(p => !p.PropertyType.FullName.Contains("Generic") &&
                            exprStartsWith.IsMatch(p.Name));
                    else
                        properties = type.GetProperties()
                            .Where(p =>
                            exprStartsWith.IsMatch(p.Name) && p.PropertyType.FullName.Contains("System")
                                && !p.PropertyType.FullName.Contains("Generic"));
                }
            }
            if (properties != null && properties.Count() > 0)
                return properties.ToList();
            else
                return null;
        }

        public static Dictionary<string, PropertyInfo> GetSelectionSetAsDictionaryOfProperties<T>(IEnumerable<string> fieldNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            Dictionary<string, PropertyInfo> selectStatement = new Dictionary<string, PropertyInfo>();
            //PropertyInfo property = null;
            foreach (var field in fieldNames)
            {
                var properties = typeof(T).GetScalarPropertyByFullNameOrStartsWith(field, fetchParentEntityAlongWithParentlId);
                if (properties != null)
                {
                    foreach (var property in properties)
                        if (property != null && !selectStatement.Keys.Contains(property.Name))
                            selectStatement.Add(property.Name, property);
                }
            }


            return selectStatement;
        }
        public static Expression<Func<T, dynamic>> DynamicSelectGeneratorAnomouysType<T>(IEnumerable<string> fieldsNames,
            bool fetchParentEntityAlongWithParentlId = false)
        {
            var sourceProperties = GetSelectionSetAsDictionaryOfProperties<T>(fieldsNames, fetchParentEntityAlongWithParentlId);

            Type dynamicType = LinqRuntimeTypeBuilder.GetDynamicType(sourceProperties.Values);

            ParameterExpression sourceItem = Expression.Parameter(typeof(T), "t");
            IEnumerable<MemberBinding> bindings = dynamicType.GetFields().Select(p => Expression.Bind(p, Expression.Property(sourceItem, sourceProperties[p.Name]))).OfType<MemberBinding>();

            var selector = Expression.Lambda<Func<T, dynamic>>(Expression.MemberInit(
                Expression.New(dynamicType.GetConstructor(Type.EmptyTypes)), bindings)
                , sourceItem);

            return selector;
        }
        public static object ToNonAnonymousList<T>(this List<T> list, Type t)
        {

            //define system Type representing List of objects of T type:
            var genericType = typeof(List<>).MakeGenericType(t);

            //create an object instance of defined type:
            var l = Activator.CreateInstance(genericType);

            //get method Add from from the list:
            MethodInfo addMethod = l.GetType().GetMethod("Add");

            //loop through the calling list:
            foreach (T item in list)
            {

                //convert each object of the list into T object 
                //by calling extension ToType<T>()
                //Add this object to newly created list:
                addMethod.Invoke(l, new object[] { item.ToType(t, t.Assembly.GetName().Name) });
            }

            //return List of T objects:
            return l;
        }

        public static object ToNonAnonymousListAsync<T>(this Task<List<T>> list, Type t)
        {

            //define system Type representing List of objects of T type:
            var genericType = typeof(List<>).MakeGenericType(t);

            //create an object instance of defined type:
            var l = Activator.CreateInstance(genericType);

            //get method Add from from the list:
            MethodInfo addMethod = l.GetType().GetMethod("Add");

            //loop through the calling list:
            foreach (T item in list.Result)
            {

                //convert each object of the list into T object 
                //by calling extension ToType<T>()
                //Add this object to newly created list:
                addMethod.Invoke(l, new object[] { item.ToType(t, t.Assembly.GetName().Name) });
            }

            //return List of T objects:
            return l;
        }

        private static object ToType<T>(this object obj, T type, string assemblyName)
        {

            //create instance of T type object:
            var tmp = Activator.CreateInstance(Type.GetType(type.ToString() + "," + assemblyName));

            //loop through the properties of the object you want to covert:          
            foreach (var pi in obj.GetType().GetFields())
            {
                try
                {

                    //get the value of property and try 
                    //to assign it to the property of T type object:
                    tmp.GetType().GetProperty(pi.Name).SetValue(tmp,
                                              pi.GetValue(obj), null);
                }
                catch { }
            }

            //return the T type object:         
            return tmp;
        }

        public static string ToTitleCase(string value)
        {
            string strTitleCase = "";
            strTitleCase = value.Substring(0, 1).ToUpper() + value.Substring(1);

            return strTitleCase;
        }

        //////////***********************////////////
        ///
        private static Expression<Func<T, R>> GetSortExpression<T, R>(string propertyName)//, R propType) //where R : Type
        {
            //the 'IN' parameter for expression ie T=> condition
            ParameterExpression pe = Expression.Parameter(typeof(T), "o");

            var exprSort = Expression.Property(pe, propertyName);
            return Expression.Lambda<Func<T, R>>(exprSort, new ParameterExpression[] { pe });
        }
       

        public static Expression<Func<TSource, dynamic>> GetDynamicGroupBy<TSource>(IEnumerable<string> groupFieldNames)
        {
            try
            {
                var sourceProperties = GetSelectionSetAsDictionaryOfProperties<TSource>(groupFieldNames);

                Type dynamicType = LinqRuntimeTypeBuilder.GetDynamicType(sourceProperties.Values);

                ParameterExpression sourceItem = Expression.Parameter(typeof(TSource), "t");
                IEnumerable<MemberBinding> bindings = dynamicType.GetFields()
                    .Select(p => Expression.Bind(p, Expression.Property(sourceItem, sourceProperties[p.Name])))
                    .OfType<MemberBinding>();

                var memberInitExpression = Expression.MemberInit(Expression.New(dynamicType), bindings);

                var keySelector = Expression.Lambda<Func<TSource, dynamic>>(memberInitExpression, sourceItem);

                return keySelector;
            }
            catch(Exception Ex)
            {

            }
            return null;
        }

        public static class LinqRuntimeTypeBuilder
        {
            //private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            private static AssemblyName assemblyName = new AssemblyName() { Name = "DynamicLinqTypes" };
            private static ModuleBuilder moduleBuilder = null;
            private static Dictionary<string, Type> builtTypes = new Dictionary<string, Type>();

            static LinqRuntimeTypeBuilder()
            {
                moduleBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName.Name);
                //moduleBuilder = Thread.GetDomain().DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName.Name);
            }

            private static string GetTypeKey(Dictionary<string, Type> fields)
            {
                //TODO: optimize the type caching -- if fields are simply reordered, that doesn't mean that they're actually different types, so this needs to be smarter
                string key = string.Empty;
                foreach (var field in fields)
                    key += field.Key + ";" + field.Value.Name + ";";

                return key;
            }

            public static Type GetDynamicType(Dictionary<string, Type> fields)
            {
                if (null == fields)
                    throw new ArgumentNullException("fields");
                if (0 == fields.Count)
                    throw new ArgumentOutOfRangeException("fields", "fields must have at least 1 field definition");

                try
                {
                    Monitor.Enter(builtTypes);
                    string className = GetTypeKey(fields);

                    if (builtTypes.ContainsKey(className))
                        return builtTypes[className];

                    TypeBuilder typeBuilder = moduleBuilder.DefineType(className, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Serializable);

                    foreach (var field in fields)
                        typeBuilder.DefineField(field.Key, field.Value, FieldAttributes.Public);

                    builtTypes[className] = typeBuilder.CreateType();

                    return builtTypes[className];
                }
                catch (Exception ex)
                {
                    //log.Error(ex);
                }
                finally
                {
                    Monitor.Exit(builtTypes);
                }

                return null;
            }


            private static string GetTypeKey(IEnumerable<PropertyInfo> fields)
            {
                return GetTypeKey(fields.ToDictionary(f => f.Name, f => f.PropertyType));
            }

            public static Type GetDynamicType(IEnumerable<PropertyInfo> fields)
            {
                return GetDynamicType(fields.ToDictionary(f => f.Name, f => f.PropertyType));
            }
        }
    }
}
