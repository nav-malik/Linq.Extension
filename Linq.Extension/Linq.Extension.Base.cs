using Linq.Extension.Filter;
using Linq.Extension.Pagination;
using System.Collections.Generic;

namespace Linq.Extension.Filter
{
    public class FilterInput
    {
        public FilterOperationEnum Operation { get; set; }
        public FilterLogicEnum Logic { get; set; }
        public string Value { get; set; }
        public string FieldName { get; set; }
        /// <summary>
        /// Provide Delimiter for Field Values, default is ",".
        /// </summary>
        public string DelimiterListOfValues { get; set; } = ",";
    }

    public class FilterGroupInput
    {
        public FilterLogicEnum Logic { get; set; }
        public List<FilterGroupInput> ChildGroups { get; set; }
        public List<FilterInput> Filters { get; set; }
    }
    public class SearchInput
    {
        //public List<FilterInput> Filters { get; set; }
        public List<FilterGroupInput> FilterGroups { get; set; }
    }
    public enum FilterOperationEnum
    {
        gte,
        gt,
        eq,
        neq,
        lt,
        lte,
        contains,
        notcontains,
        startswith,
        endswith,
        inlist,
        notinlist,
        notstartswith,
        notendswith,
        containsinlist,
        notcontainsinlist,
        startswithinlist,
        endswithinlist,
        notstartswithinlist,
        notendswithinlist
    }
    public enum FilterLogicEnum
    {
        and,
        or
    }
}

namespace Linq.Extension.Pagination
{
    public class PaginationInput
    {
        public int? Take { get; set; }
        public int? Skip { get; set; }
        public List<SortInput> Sorts { get; set; }
    }

    public class SortInput
    {
        public string FieldName { get; set; }
        public SortDirectionEnum Direction { get; set; }
    }

    public enum SortDirectionEnum
    {
        asc,
        desc
    }
}

namespace Linq.Extension.Unique
{
    public class DistinctByInput
    {
        /// <summary>
        /// Delimited separated field names.
        /// </summary>
        public string FieldNames { get; set; }

        /// <summary>
        /// Provide Delimiter for Field Names, default is ",".
        /// </summary>
        public string DelimiterFieldNames { get; set; } = ",";
        /// <summary>
        /// Provide Delimiter for Field Values, default is ",".
        /// </summary>
        public string DelimiterFieldValues { get; set; } = ",";

        /// <summary>
        /// Provide search object to filter data for DistinctBy.
        /// </summary>
        public SearchInput Search { get; set; }

        /// <summary>
        /// Provide pagination object to apply sort, take and skip on data for DistinctBy.
        /// </summary>
        public PaginationInput Pagination {  get; set; }
    }
}

namespace Linq.Extension.Grouping
{
    public class GroupByInput
    {
        /// <summary>
        /// Delimited separated field names.
        /// </summary>
        public string FieldNames { get; set; }

        /// <summary>
        /// Provide Delimiter for Field Names, default is ",".
        /// </summary>
        public string DelimiterFieldNames { get; set; } = ",";        

        /// <summary>
        /// Provide search object to filter data for GroupBy.
        /// </summary>
        public SearchInput Search { get; set; }
    }

    public class GroupByOperationOnInput
    {
        /// <summary>
        /// Delimited separated field names.
        /// </summary>
        public string GroupByFieldNames { get; set; }

        /// <summary>
        /// Single field name.
        /// </summary>
        public string OperationOnFieldName { get; set; }

        /// <summary>
        /// Provide Delimiter for Field Names, default is ",".
        /// </summary>
        public string DelimiterFieldNames { get; set; } = ",";
        /// <summary>
        /// Provide Delimiter for Field Values, default is ",".
        /// </summary>
        public string DelimiterFieldValues { get; set; } = ",";

        /// <summary>
        /// Operations can be sum, count, min and max, default is count.
        /// </summary>
        public GroupByOperationEnum Operation { get; set; }

        /// <summary>
        /// Provide search object to filter data for GroupBy fields.
        /// </summary>
        public SearchInput Search { get; set; }

        /// <summary>
        /// Provide pagination object to apply sort, take and skip on data for GroupBy fields.
        /// </summary>
        public PaginationInput Pagination { get; set; }
    }

    public enum GroupByOperationEnum
    {
        count,
        sum,
        min,
        max
    }

    public class GroupValuePair
    {
        public List<GroupKeyNameValue> Keys { get; set; }
        public int Value { get; set; }
    }

    public class GroupKeyNameValue
    {
        public string KeyName { get; set; }
        public string KeyValue {get; set;}
    }
}
