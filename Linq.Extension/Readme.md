
4.5.0 Added GroupByAggregation, that will allow to perfom Count Distinct, Count, Sum, Min, and Max on a field other than in GroupBy field name list.
Also, update the GroupByInput and DistinctByInput, instead of passing field names as delimated string now field names will be List of strings.

4.0.0 Added supoorted Frameworks Net6.0, Net8.0 and Framework 4.8. Also, added new extension method DistinctBy for .Net Framework.

3.8.1 Added Delimiter for field values in FilterInput for list type of operation. Default value for delimiters will be comma (,) and these fields are string, so we can pass more than one character for delimiter, this is particularly helpful in field values as comma (,) can be in the values it self.

3.8.0 Added Delimiter for field names and field values for DistinctBy, GroupBy, and GroupByOperationOn. Default value for delimiters will be comma (,) and these fields are string, so we can pass more than one character for delimiter, this is particularly helpful in field values as comma (,) can be in the values it self.

3.7.0.1 Changed parameters type to IDictionary in GetListOfGroupValuePair

3.7.0 Added GroupByOperationOn classes and methods. This will provide option to group by on multiple fields (, separated fields) and then perform an operation like count, sum, max or min on another field. Also provide option to apply where and pagination on group by.

3.6.5 Added overloaded methods for Where and WhereWithDistinctBy for RelationIds

3.6.4 Added overloaded methods for DistinctBySelect, DistinctBy, GetSearch, GetPagination, GetDistinctBy, and GetGroupBy methods.

3.6.3 Added GetSearch, GetPagination, GetDistinctBy, GetGroupBy methods.

3.6.2 Added overloaded extension method for DistinctBy which will take dictionary of objects with one of type DistinctByInput or pass DistinctByInput and if that object of DistinctByInput contains Search and Pagination properties then apply them. Also, applied search in GroupBy if groupby object contains Search property.

3.6.1 Added overloaded extension methods for GroupBy and fix error in DistinctIf to check if key is present in Dictionary

3.6.0 Added dynamic GroupBy which takes field names as list of string or delimited strings.

3.5.1 Added overloaded Extension methods for WhereWithDistinctBy, SortBy, Take, Skip and DisinctIf to take dictionary of string and object pair to get DistinctBy object or Pagination object instead of passing actual type.

3.5.0 Added Where Extension Methods. Added EF Core DistinctBy Extension Method which List of original type, also added WhereWithDistinctBy which will fetch list of data from distinct by and convert into where expression and then combine it with rest of where expression to make one combine where predicate.

3.4.0 Added DistinctBySelect which takes comma separted fieldnames as string or list of string for fieldnames and generate distinct select query on input fields only. Also, added distinct extension method which takes bool input distinct with default to true and generate distinct query. Aslo, added distinct bool input to Pagination extension method with default to false and it applies distinct if input is true before sort, take and skip.

3.3.1 Added Select extension methods for Dynamic Select Generator as actual type of Entity or Dynamic type.

3.3.0 Added Dynamic Select Generator expression which return actual type instead of Dynamic type, also added IEnumerable DistinctBy.

Updated Dynamic Where Predicate. Now generated sql will not have case statements in where condition.

Added Child Groups in FilterGroupInput this will allow to create nested 'Parenthesis' () in generated SQL queries. ChildGroups is type of List of FilterGroupInput so it'll generated 'Parenthesis' () with in 'Parenthesis' () recursively. Excluded the proprties which doesn't exists in the resultset in SortBy extension method. Also, in case of exception return the source. Fix the sort on nullable fields. Added dynamic Group By with list of strings as names of the fields Group By on. Major change. Added Filter Groups. This allow to create SQL queries with 'Parenthesis' () and separate And/Or groups with () in SQL.