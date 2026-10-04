/*
  Performance measurement for the main API queries on the seeded 100,000 rows.
  The SQL is copied from the EF Core command log (Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information)
  and executed with sp_executesql, like EF does, so the plans are the same as in the application.

  Each query runs twice:
    [idx]  – normal execution, the optimizer may use the indexes
    [scan] – WITH (INDEX(1)) forces a clustered index scan = "what it would cost without the index"

  Run:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d RequestsManagement -E -i measure.sql
  Read: "Table 'Requests'. Scan count X, logical reads Y" and "CPU time / elapsed time" for each block.
*/
SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '=== Q1 [idx] default list: page 1 ORDER BY CreatedAt DESC';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r]';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@p int, @p1 int', 0, 20;
PRINT '=== Q1 [scan]';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WITH (INDEX(1)) ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@p int, @p1 int', 0, 20;
GO

PRINT '=== Q2 [idx] status IN (New, Waiting): count + page 1';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Status] IN (@s1, @s2)', N'@s1 tinyint, @s2 tinyint', 0, 2;
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Status] IN (@s1, @s2) ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p2 ROWS ONLY',
N'@s1 tinyint, @s2 tinyint, @p int, @p2 int', 0, 2, 0, 20;
PRINT '=== Q2 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[Status] IN (@s1, @s2)', N'@s1 tinyint, @s2 tinyint', 0, 2;
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[Status] IN (@s1, @s2) ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p2 ROWS ONLY',
N'@s1 tinyint, @s2 tinyint, @p int, @p2 int', 0, 2, 0, 20;
GO

PRINT '=== Q3 [idx] deep page: status = New, page 500 (OFFSET 9980)';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Status] = @s1 ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p2 ROWS ONLY',
N'@s1 tinyint, @p int, @p2 int', 0, 9980, 20;
GO

PRINT '=== Q4 [idx] text search contains ''permit'' (Title OR OrganizationName)';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Title] LIKE @t ESCAPE N''\'' OR [r].[OrganizationName] LIKE @t ESCAPE N''\''', N'@t nvarchar(200)', N'%permit%';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Title] LIKE @t ESCAPE N''\'' OR [r].[OrganizationName] LIKE @t ESCAPE N''\''
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p3 ROWS ONLY', N'@t nvarchar(200), @p int, @p3 int', N'%permit%', 0, 20;
GO

PRINT '=== Q5 [idx] assignedTo = agent07 AND status = InProgress, ORDER BY Priority';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[Status] = @s1 AND [r].[AssignedTo] = @a', N'@s1 tinyint, @a nvarchar(100)', 1, N'agent07';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[Status] = @s1 AND [r].[AssignedTo] = @a ORDER BY [r].[Priority] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p3 ROWS ONLY',
N'@s1 tinyint, @a nvarchar(100), @p int, @p3 int', 1, N'agent07', 0, 20;
PRINT '=== Q5 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[Status] = @s1 AND [r].[AssignedTo] = @a', N'@s1 tinyint, @a nvarchar(100)', 1, N'agent07';
GO

PRINT '=== Q6 [idx] organization prefix ''Negev'' + CreatedAt range (Q1 2026)';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3)', N'Negev%', '2026-01-01', '2026-03-31T23:59:59';
EXEC sp_executesql N'SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r] WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC OFFSET @p ROWS FETCH NEXT @p4 ROWS ONLY',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3), @p int, @p4 int', N'Negev%', '2026-01-01', '2026-03-31T23:59:59', 0, 20;
PRINT '=== Q6 [scan]';
EXEC sp_executesql N'SELECT COUNT(*) FROM [Requests] AS [r] WITH (INDEX(1)) WHERE [r].[OrganizationName] LIKE @o ESCAPE N''\'' AND [r].[CreatedAt] >= @f AND [r].[CreatedAt] <= @t',
N'@o nvarchar(200), @f datetime2(3), @t datetime2(3)', N'Negev%', '2026-01-01', '2026-03-31T23:59:59';
GO

PRINT '=== Q7 [idx] summary: GROUP BY Status, Priority';
EXEC sp_executesql N'SELECT [r].[Status], [r].[Priority], COUNT(*) AS [Count] FROM [Requests] AS [r] GROUP BY [r].[Status], [r].[Priority]';
PRINT '=== Q7 [scan]';
EXEC sp_executesql N'SELECT [r].[Status], [r].[Priority], COUNT(*) AS [Count] FROM [Requests] AS [r] WITH (INDEX(1)) GROUP BY [r].[Status], [r].[Priority]';
GO

PRINT '=== Q8 [idx] summary: top 5 assignees by open requests';
EXEC sp_executesql N'SELECT TOP(@p) [r].[AssignedTo] AS [Assignee], COUNT(*) AS [Count] FROM [Requests] AS [r]
WHERE [r].[Status] <> CAST(3 AS tinyint) AND [r].[AssignedTo] IS NOT NULL GROUP BY [r].[AssignedTo] ORDER BY COUNT(*) DESC, [r].[AssignedTo]', N'@p int', 5;
PRINT '=== Q8 [scan]';
EXEC sp_executesql N'SELECT TOP(@p) [r].[AssignedTo] AS [Assignee], COUNT(*) AS [Count] FROM [Requests] AS [r] WITH (INDEX(1))
WHERE [r].[Status] <> CAST(3 AS tinyint) AND [r].[AssignedTo] IS NOT NULL GROUP BY [r].[AssignedTo] ORDER BY COUNT(*) DESC, [r].[AssignedTo]', N'@p int', 5;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO
