using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Polars;

public sealed class PolarsSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePolars() => await PythonEnvironment.RequirePolarsAsync();

    // ── test_basic_expr ───────────────────────────────────────────────────

    [Test]
    public async Task Polars_BasicExpr_AddsColumnCWithCorrectValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_basic_expr():
                df = pl.DataFrame({"a":[1,2,3], "b":[4,5,6]})
                result = df.with_columns((pl.col("a") + pl.col("b")).alias("c"))
                return int(result.height), int(result["c"][0]), int(result["c"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_basic_expr");
        using var height = result[0L];
        using var c0 = result[1L];
        using var c2 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(3);
        await Assert.That(c0.As<int>()).IsEqualTo(5);
        await Assert.That(c2.As<int>()).IsEqualTo(9);
    }

    // ── test_filter ───────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Filter_TwoRowsGreaterThan2()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_filter():
                df = pl.DataFrame({"a":[1,2,3,4]})
                out = df.filter(pl.col("a") > 2)
                return int(out.height), int(out["a"][0]), int(out["a"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_filter");
        using var height = result[0L];
        using var a0 = result[1L];
        using var a1 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(2);
        await Assert.That(a0.As<int>()).IsEqualTo(3);
        await Assert.That(a1.As<int>()).IsEqualTo(4);
    }

    // ── test_groupby ──────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Groupby_SumPerGroupCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_groupby():
                df = pl.DataFrame({"g":["a","a","b","b"], "v":[1,2,3,4]})
                agg = df.group_by("g").agg(pl.col("v").sum()).sort("g")
                return int(agg["v"][0]), int(agg["v"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_groupby");
        using var aSum = result[0L];
        using var bSum = result[1L];

        await Assert.That(aSum.As<int>()).IsEqualTo(3);
        await Assert.That(bSum.As<int>()).IsEqualTo(7);
    }

    // ── test_join ─────────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Join_InnerJoinTwoRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_join():
                df1 = pl.DataFrame({"key":["a","b","c"], "v1":[1,2,3]})
                df2 = pl.DataFrame({"key":["b","c","d"], "v2":[10,20,30]})
                out = df1.join(df2, on="key", how="inner")
                return int(out.height), int(out["v1"][0]), int(out["v2"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_join");
        using var height = result[0L];
        using var v10 = result[1L];
        using var v21 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(2);
        await Assert.That(v10.As<int>()).IsEqualTo(2);
        await Assert.That(v21.As<int>()).IsEqualTo(20);
    }

    // ── test_lazy_api ─────────────────────────────────────────────────────

    [Test]
    public async Task Polars_LazyApi_FilterAndCollect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_lazy_api():
                df = pl.DataFrame({"a":[1,2,3,4,5]})
                result = df.lazy().filter(pl.col("a") >= 3).collect()
                return int(result.height), int(result["a"][0]), int(result["a"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_lazy_api");
        using var height = result[0L];
        using var a0 = result[1L];
        using var a2 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(3);
        await Assert.That(a0.As<int>()).IsEqualTo(3);
        await Assert.That(a2.As<int>()).IsEqualTo(5);
    }

    // ── test_window_function ──────────────────────────────────────────────

    [Test]
    public async Task Polars_WindowFunction_GroupCumulativeSumCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_window_function():
                df = pl.DataFrame({"g":["a","a","b","b"], "v":[1,2,3,4]})
                result = df.with_columns(
                    pl.col("v").cum_sum().over("g").alias("cs")
                )
                return int(result["cs"][0]), int(result["cs"][1]), int(result["cs"][2]), int(result["cs"][3])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_window_function");
        using var cs0 = result[0L];
        using var cs1 = result[1L];
        using var cs2 = result[2L];
        using var cs3 = result[3L];

        await Assert.That(cs0.As<int>()).IsEqualTo(1);  // a: 1
        await Assert.That(cs1.As<int>()).IsEqualTo(3);  // a: 1+2
        await Assert.That(cs2.As<int>()).IsEqualTo(3);  // b: 3
        await Assert.That(cs3.As<int>()).IsEqualTo(7);  // b: 3+4
    }

    // ── test_string_ops ───────────────────────────────────────────────────

    [Test]
    public async Task Polars_StringOps_UpperAndLenCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_string_ops():
                df = pl.DataFrame({"name":["alice","bob","carol"]})
                result = df.with_columns([
                    pl.col("name").str.to_uppercase().alias("upper"),
                    pl.col("name").str.len_chars().alias("length")
                ])
                return str(result["upper"][0]), int(result["length"][0]), int(result["length"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_string_ops");
        using var upper0 = result[0L];
        using var len0 = result[1L];
        using var len1 = result[2L];

        await Assert.That(upper0.As<string>()).IsEqualTo("ALICE");
        await Assert.That(len0.As<int>()).IsEqualTo(5);
        await Assert.That(len1.As<int>()).IsEqualTo(3);
    }

    // ── test_datetime_ops ─────────────────────────────────────────────────

    [Test]
    public async Task Polars_DatetimeOps_ExtractsYearAndMonth()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            from datetime import date
            def test_datetime_ops():
                df = pl.DataFrame({"dt": [date(2024,1,15), date(2024,3,20)]})
                result = df.with_columns([
                    pl.col("dt").dt.year().alias("year"),
                    pl.col("dt").dt.month().alias("month")
                ])
                return int(result["year"][0]), int(result["month"][0]), int(result["month"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_datetime_ops");
        using var year0 = result[0L];
        using var month0 = result[1L];
        using var month1 = result[2L];

        await Assert.That(year0.As<int>()).IsEqualTo(2024);
        await Assert.That(month0.As<int>()).IsEqualTo(1);
        await Assert.That(month1.As<int>()).IsEqualTo(3);
    }

    // ── test_pivot ────────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Pivot_CorrectShapeAndCellValue()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_pivot():
                df = pl.DataFrame({
                    "row": ["r1","r1","r2","r2"],
                    "col": ["c1","c2","c1","c2"],
                    "val": [10,20,30,40]
                })
                piv = df.pivot(on="col", index="row", values="val")
                return int(piv.height), int(piv.width), int(piv.filter(pl.col("row")=="r1")["c1"][0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_pivot");
        using var height = result[0L];
        using var width = result[1L];
        using var r1c1 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(2);
        await Assert.That(width.As<int>()).IsEqualTo(3); // "row" + "c1" + "c2"
        await Assert.That(r1c1.As<int>()).IsEqualTo(10);
    }

    // ── test_null_handling ────────────────────────────────────────────────

    [Test]
    public async Task Polars_NullHandling_FilledValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_null_handling():
                df = pl.DataFrame({"a":[1,None,3], "b":[None,5,None]})
                result = df.with_columns([
                    pl.col("a").fill_null(0),
                    pl.col("b").fill_null(99)
                ])
                return int(result["a"][1]), int(result["b"][0]), int(result["b"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_null_handling");
        using var a1 = result[0L];
        using var b0 = result[1L];
        using var b2 = result[2L];

        await Assert.That(a1.As<int>()).IsEqualTo(0);
        await Assert.That(b0.As<int>()).IsEqualTo(99);
        await Assert.That(b2.As<int>()).IsEqualTo(99);
    }

    // ── test_sort ─────────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Sort_DescendingFirstValueIsLargest()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_sort():
                df = pl.DataFrame({"a":[3,1,4,1,5,9,2,6]})
                out = df.sort("a", descending=True)
                return int(out["a"][0]), int(out["a"][-1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_sort");
        using var first = result[0L];
        using var last = result[1L];

        await Assert.That(first.As<int>()).IsEqualTo(9);
        await Assert.That(last.As<int>()).IsEqualTo(1);
    }

    // ── test_schema ───────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Schema_ColumnTypesReported()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_schema():
                df = pl.DataFrame({"a":[1,2,3], "b":[1.0,2.0,3.0], "c":["x","y","z"]})
                schema = df.schema
                return str(schema["a"]), str(schema["b"]), str(schema["c"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_schema");
        using var typeA = result[0L];
        using var typeB = result[1L];
        using var typeC = result[2L];

        await Assert.That(typeA.As<string>()).IsEqualTo("Int64");
        await Assert.That(typeB.As<string>()).IsEqualTo("Float64");
        await Assert.That(typeC.As<string>()).IsEqualTo("String");
    }

    // ── test_with_columns_multi ───────────────────────────────────────────

    [Test]
    public async Task Polars_WithColumnsMulti_AllThreeColumnsAdded()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_with_columns_multi():
                df = pl.DataFrame({"a":[1,2,3], "b":[4,5,6]})
                result = df.with_columns([
                    (pl.col("a") + pl.col("b")).alias("c"),
                    (pl.col("a") * pl.col("b")).alias("d"),
                    (pl.col("b") - pl.col("a")).alias("e")
                ])
                return int(result.width), int(result["c"][0]), int(result["d"][1]), int(result["e"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_with_columns_multi");
        using var width = result[0L];
        using var c0 = result[1L];
        using var d1 = result[2L];
        using var e2 = result[3L];

        await Assert.That(width.As<int>()).IsEqualTo(5);
        await Assert.That(c0.As<int>()).IsEqualTo(5);  // 1+4
        await Assert.That(d1.As<int>()).IsEqualTo(10); // 2*5
        await Assert.That(e2.As<int>()).IsEqualTo(3);  // 6-3
    }

    // ── test_select ───────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Select_SelectSubsetOfColumns()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_select():
                df = pl.DataFrame({"a":[1,2], "b":[3,4], "c":[5,6]})
                out = df.select(["a","c"])
                return int(out.width), list(out.columns)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_select");
        using var width = result[0L];
        using var cols = result[1L];

        await Assert.That(width.As<int>()).IsEqualTo(2);
        var colArr = cols.As<string[]>();
        await Assert.That(colArr[0]).IsEqualTo("a");
        await Assert.That(colArr[1]).IsEqualTo("c");
    }

    // ── test_unique ───────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Unique_RemovesDuplicates()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_unique():
                df = pl.DataFrame({"a":[1,1,2,2,3]})
                out = df.unique().sort("a")
                return int(out.height), int(out["a"][0]), int(out["a"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_unique");
        using var height = result[0L];
        using var a0 = result[1L];
        using var a2 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(3);
        await Assert.That(a0.As<int>()).IsEqualTo(1);
        await Assert.That(a2.As<int>()).IsEqualTo(3);
    }

    // ── test_rename ───────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Rename_ColumnNameChangedInSchema()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_rename():
                df = pl.DataFrame({"old_name":[1,2,3]})
                out = df.rename({"old_name": "new_name"})
                return list(out.columns), int(out["new_name"][0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_rename");
        using var cols = result[0L];
        using var v0 = result[1L];

        var colArr = cols.As<string[]>();
        await Assert.That(colArr[0]).IsEqualTo("new_name");
        await Assert.That(v0.As<int>()).IsEqualTo(1);
    }

    // ── test_describe ─────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Describe_StatisticsCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_describe():
                df = pl.DataFrame({"v":[1.0,2.0,3.0,4.0,5.0]})
                desc = df.describe()
                # Polars describe has rows: count, null_count, mean, std, min, 25%, 50%, 75%, max
                mean_row = desc.filter(pl.col("statistic") == "mean")
                return float(mean_row["v"][0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_describe");

        await Assert.That(result.As<double>()).IsEqualTo(3.0);
    }

    // ── test_explode ──────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Explode_ListColumnExpandsToFiveRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_explode():
                df = pl.DataFrame({"id":[1,2], "vals":[[10,20,30],[40,50]]})
                out = df.explode("vals")
                return int(out.height), int(out["vals"][0]), int(out["vals"][4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_explode");
        using var height = result[0L];
        using var v0 = result[1L];
        using var v4 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(5);
        await Assert.That(v0.As<int>()).IsEqualTo(10);
        await Assert.That(v4.As<int>()).IsEqualTo(50);
    }

    // ── test_melt ─────────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Melt_WideToLongHasFourRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_melt():
                df = pl.DataFrame({"id":[1,2], "x":[10,20], "y":[30,40]})
                out = df.unpivot(on=["x","y"], index="id")
                return int(out.height), str(out["variable"][0]), str(out["variable"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_melt");
        using var height = result[0L];
        using var var0 = result[1L];
        using var var2 = result[2L];

        await Assert.That(height.As<int>()).IsEqualTo(4);
        await Assert.That(var0.As<string>()).IsEqualTo("x");
        await Assert.That(var2.As<string>()).IsEqualTo("y");
    }

    // ── test_cast ─────────────────────────────────────────────────────────

    [Test]
    public async Task Polars_Cast_Int64ToFloat64()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_cast():
                df = pl.DataFrame({"a":[1,2,3]})
                out = df.with_columns(pl.col("a").cast(pl.Float64))
                return str(out.dtypes[0]), float(out["a"][0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_cast");
        using var dtype = result[0L];
        using var v0 = result[1L];

        await Assert.That(dtype.As<string>()).IsEqualTo("Float64");
        await Assert.That(v0.As<double>()).IsEqualTo(1.0);
    }

    // ── test_categorical ──────────────────────────────────────────────────

    [Test]
    public async Task Polars_Categorical_DtypeIsCategorical()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import polars as pl
            def test_categorical():
                df = pl.DataFrame({"color":["red","blue","red","green"]})
                out = df.with_columns(pl.col("color").cast(pl.Categorical))
                return str(out.dtypes[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_categorical");

        await Assert.That(result.As<string>()).IsEqualTo("Categorical");
    }
}
