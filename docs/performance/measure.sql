/*
  Performance measurement of the main API queries on the seeded 100,000 requests.
  The SQL is copied from the EF Core 8 command log
  (Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information) and run with sp_executesql
  and parameters, like EF does, so the plans match the application.

  Each query runs as:
    [idx]  – normal execution (the optimizer may use the indexes)
    [scan] – WITH (INDEX(1)): forces a clustered index scan = "what it would cost without the index"

  Run:   sqlcmd -S "(localdb)\MSSQLLocalDB" -d RequestsManagement -E -i docs\performance\measure.sql
  Read:  "Table 'Requests'. Scan count X, logical reads Y" and "CPU time / elapsed time" per block.
  Plans: replace the first SET line with SET STATISTICS PROFILE ON.
*/
SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '=== Q1 [idx] list, no filter: COUNT + page 1 ORDER BY CreatedAt DESC';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r]';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY', N'@p0 int, @p1 int', 0, 20;
PRINT '=== Q1 [scan]';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WITH (INDEX(1)) ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY', N'@p0 int, @p1 int', 0, 20;
GO

PRINT '=== Q2 [idx] status New or Waiting: COUNT + page 1';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Status] = @s0 OR [r].[Status] = @s1', N'@s0 tinyint, @s1 tinyint', 0, 2;
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Status] = @s0 OR [r].[Status] = @s1 ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY',
N'@s0 tinyint, @s1 tinyint, @p0 int, @p1 int', 0, 2, 0, 20;
PRINT '=== Q2 [openjson] the same COUNT as EF Core 8 generates by default for values.Contains(field)';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Status] IN (SELECT [s].[value] FROM OPENJSON(@s) WITH ([value] tinyint ''$'') AS [s])', N'@s nvarchar(4000)', N'[0,2]';
PRINT '=== Q2 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[Status] = @s0 OR [r].[Status] = @s1', N'@s0 tinyint, @s1 tinyint', 0, 2;
GO

PRINT '=== Q3 [idx] deep page: status New, page 500 (OFFSET 9980)';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Status] = @s0 ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY',
N'@s0 tinyint, @p0 int, @p1 int', 0, 9980, 20;
GO

PRINT '=== Q4 [idx] text search contains N''היתר'' (Title OR OrganizationName): COUNT + page 1';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Title] LIKE @t ESCAPE N''\'' OR [r].[OrganizationName] LIKE @t ESCAPE N''\''', N'@t nvarchar(200)', N'%היתר%';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Title] LIKE @t ESCAPE N''\'' OR [r].[OrganizationName] LIKE @t ESCAPE N''\''
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY', N'@t nvarchar(200), @p0 int, @p1 int', N'%היתר%', 0, 20;
GO

PRINT '=== Q5 [idx] handler contains N''לוי'' + status InProgress, ORDER BY Priority: COUNT + page 1';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[AssignedTo] LIKE @a ESCAPE N''\'' AND [r].[Status] = @s0', N'@a nvarchar(100), @s0 tinyint', N'%לוי%', 1;
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[AssignedTo] LIKE @a ESCAPE N''\'' AND [r].[Status] = @s0 ORDER BY [r].[Priority] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY',
N'@a nvarchar(100), @s0 tinyint, @p0 int, @p1 int', N'%לוי%', 1, 0, 20;
PRINT '=== Q5 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[AssignedTo] LIKE @a ESCAPE N''\'' AND [r].[Status] = @s0', N'@a nvarchar(100), @s0 tinyint', N'%לוי%', 1;
GO

PRINT '=== Q6 [idx] organization starts with N''נגב'' + CreatedAt in Q1 2026: COUNT + page 1';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3)', N'נגב%', '2026-01-01', '2026-03-31T23:59:59';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3), @p0 int, @p1 int', N'נגב%', '2026-01-01', '2026-03-31T23:59:59', 0, 20;
PRINT '=== Q6 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3)', N'נגב%', '2026-01-01', '2026-03-31T23:59:59';
GO

PRINT '=== Q7 [idx] summary buckets: GROUP BY Status, Priority with age and last-update aggregates';
EXEC sp_executesql N'SELECT [r].[Status], [r].[Priority], COUNT(*), COUNT(CASE WHEN [r].[CreatedAt] < @cut THEN 1 END), MAX([r].[UpdatedAt])
FROM [Requests] AS [r] GROUP BY [r].[Status], [r].[Priority]', N'@cut datetime2(3)', '2026-09-27';
PRINT '=== Q7 [scan]';
EXEC sp_executesql N'SELECT [r].[Status], [r].[Priority], COUNT(*), COUNT(CASE WHEN [r].[CreatedAt] < @cut THEN 1 END), MAX([r].[UpdatedAt])
FROM [Requests] AS [r] WITH (INDEX(1)) GROUP BY [r].[Status], [r].[Priority]', N'@cut datetime2(3)', '2026-09-27';
GO

PRINT '=== Q8 [idx] summary: top 5 handlers by open requests';
EXEC sp_executesql N'SELECT TOP(@n) [r].[AssignedTo], COUNT(*) FROM [Requests] AS [r]
WHERE [r].[Status] <> CAST(3 AS tinyint) AND [r].[AssignedTo] IS NOT NULL GROUP BY [r].[AssignedTo] ORDER BY COUNT(*) DESC, [r].[AssignedTo]', N'@n int', 5;
PRINT '=== Q8 [scan]';
EXEC sp_executesql N'SELECT TOP(@n) [r].[AssignedTo], COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1))
WHERE [r].[Status] <> CAST(3 AS tinyint) AND [r].[AssignedTo] IS NOT NULL GROUP BY [r].[AssignedTo] ORDER BY COUNT(*) DESC, [r].[AssignedTo]', N'@n int', 5;
GO

PRINT '=== Q9 [idx] conditional status update (as EF sends it) + audit insert – rolled back';
BEGIN TRANSACTION;
DECLARE @rv binary(8) = (SELECT RowVersion FROM Requests WHERE Id = 50000);
EXEC sp_executesql N'UPDATE [Requests] SET [Status] = @p0, [UpdatedAt] = @p1 OUTPUT INSERTED.[RowVersion] WHERE [Id] = @p2 AND [RowVersion] = @p3',
N'@p0 tinyint, @p1 datetime2(3), @p2 int, @p3 varbinary(8)', 1, '2026-10-04T12:00:00', 50000, @rv;
EXEC sp_executesql N'INSERT INTO [RequestStatusHistory] ([ChangedAt], [ChangedBy], [NewStatus], [PreviousStatus], [RequestId]) OUTPUT INSERTED.[Id] VALUES (@p0, @p1, @p2, @p3, @p4)',
N'@p0 datetime2(3), @p1 nvarchar(100), @p2 tinyint, @p3 tinyint, @p4 int', '2026-10-04T12:00:00', N'מדידה', 1, 0, 50000;
ROLLBACK TRANSACTION;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO
