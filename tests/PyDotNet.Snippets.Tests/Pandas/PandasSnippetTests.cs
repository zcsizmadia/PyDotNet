using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Pandas;

public sealed class PandasSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.RequirePandasAsync();

    // ── pandas_basic ──────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Basic_TwoByTwoDataFrame()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def pandas_basic():
                df = pd.DataFrame({"a": [1,2], "b": [3,4]})
                return int(len(df)), int(df["a"][0]), int(df["b"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("pandas_basic");
        using var rows = result[0L];
        using var a0 = result[1L];
        using var b1 = result[2L];

        await Assert.That(rows.As<int>()).IsEqualTo(2);
        await Assert.That(a0.As<int>()).IsEqualTo(1);
        await Assert.That(b1.As<int>()).IsEqualTo(4);
    }

    // ── pandas_sum ────────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Sum_ScalarColumnSumCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def pandas_sum():
                df = pd.DataFrame({"x": [10, 20, 30]})
                return int(df["x"].sum())
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("pandas_sum");

        await Assert.That(result.As<int>()).IsEqualTo(60);
    }

    // ── pandas_filter ─────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Filter_TwoRowsGreaterThan2()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def pandas_filter():
                df = pd.DataFrame({"a": [1,2,3,4]})
                out = df[df["a"] > 2]
                return int(len(out)), int(out["a"].iloc[0]), int(out["a"].iloc[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("pandas_filter");
        using var count = result[0L];
        using var first = result[1L];
        using var second = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(2);
        await Assert.That(first.As<int>()).IsEqualTo(3);
        await Assert.That(second.As<int>()).IsEqualTo(4);
    }

    // ── pandas_group ──────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Group_SumsByGroupKey()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def pandas_group():
                df = pd.DataFrame({"g":["a","a","b","b"], "v":[1,2,3,4]})
                agg = df.groupby("g")["v"].sum()
                return int(agg["a"]), int(agg["b"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("pandas_group");
        using var aSum = result[0L];
        using var bSum = result[1L];

        await Assert.That(aSum.As<int>()).IsEqualTo(3);
        await Assert.That(bSum.As<int>()).IsEqualTo(7);
    }

    // ── pandas_to_dict ────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_ToDict_ContainsBothColumnKeys()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def pandas_to_dict():
                df = pd.DataFrame({"a":[1,2], "b":[3,4]})
                d = df.to_dict()
                return "a" in d, "b" in d, int(d["a"][0]), int(d["b"][1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("pandas_to_dict");
        using var hasA = result[0L];
        using var hasB = result[1L];
        using var a0 = result[2L];
        using var b1 = result[3L];

        await Assert.That(hasA.As<bool>()).IsTrue();
        await Assert.That(hasB.As<bool>()).IsTrue();
        await Assert.That(a0.As<int>()).IsEqualTo(1);
        await Assert.That(b1.As<int>()).IsEqualTo(4);
    }

    // ── test_basic_df_arithmetic ──────────────────────────────────────────

    [Test]
    public async Task Pandas_BasicDfArithmetic_ColumnCHasCorrectValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_basic_df_arithmetic():
                df = pd.DataFrame({"a":[1,2,3], "b":[4,5,6]})
                df["c"] = df["a"] + df["b"]
                return int(df["c"][0]), int(df["c"][1]), int(df["c"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_basic_df_arithmetic");
        using var c0 = result[0L];
        using var c1 = result[1L];
        using var c2 = result[2L];

        await Assert.That(c0.As<int>()).IsEqualTo(5);
        await Assert.That(c1.As<int>()).IsEqualTo(7);
        await Assert.That(c2.As<int>()).IsEqualTo(9);
    }

    // ── test_filter_conditions ────────────────────────────────────────────

    [Test]
    public async Task Pandas_FilterConditions_TwoRowsReturnedWhenBothTrue()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_filter_conditions():
                df = pd.DataFrame({"a":[1,2,3,4], "b":[5,6,7,8]})
                out = df[(df["a"] > 1) & (df["b"] < 8)]
                return int(len(out)), int(out["a"].iloc[0]), int(out["a"].iloc[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_filter_conditions");
        using var count = result[0L];
        using var first = result[1L];
        using var second = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(2);
        await Assert.That(first.As<int>()).IsEqualTo(2);
        await Assert.That(second.As<int>()).IsEqualTo(3);
    }

    // ── test_groupby_agg ──────────────────────────────────────────────────

    [Test]
    public async Task Pandas_GroupbyAgg_SumAndMeanPerGroup()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_groupby_agg():
                df = pd.DataFrame({"g":["a","a","b","b"], "v":[1.0,2.0,3.0,4.0]})
                agg = df.groupby("g")["v"].agg(["sum","mean"])
                return float(agg.loc["a","sum"]), float(agg.loc["a","mean"]), float(agg.loc["b","sum"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_groupby_agg");
        using var aSum = result[0L];
        using var aMean = result[1L];
        using var bSum = result[2L];

        await Assert.That(aSum.As<double>()).IsEqualTo(3.0);
        await Assert.That(aMean.As<double>()).IsEqualTo(1.5);
        await Assert.That(bSum.As<double>()).IsEqualTo(7.0);
    }

    // ── test_merge_outer ──────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Merge_OuterJoinFourRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_merge_outer():
                df1 = pd.DataFrame({"key":["a","b","c"], "v1":[1,2,3]})
                df2 = pd.DataFrame({"key":["b","c","d"], "v2":[10,20,30]})
                merged = df1.merge(df2, on="key", how="outer")
                return int(len(merged))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_merge_outer");

        await Assert.That(result.As<int>()).IsEqualTo(4);
    }

    // ── test_pivot ────────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Pivot_CorrectShapeAndValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_pivot():
                df = pd.DataFrame({
                    "row": ["r1","r1","r2","r2"],
                    "col": ["c1","c2","c1","c2"],
                    "val": [10.0,20.0,30.0,40.0]
                })
                piv = df.pivot(index="row", columns="col", values="val")
                return int(piv.shape[0]), int(piv.shape[1]), float(piv.loc["r1","c1"]), float(piv.loc["r2","c2"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_pivot");
        using var rows = result[0L];
        using var cols = result[1L];
        using var r1c1 = result[2L];
        using var r2c2 = result[3L];

        await Assert.That(rows.As<int>()).IsEqualTo(2);
        await Assert.That(cols.As<int>()).IsEqualTo(2);
        await Assert.That(r1c1.As<double>()).IsEqualTo(10.0);
        await Assert.That(r2c2.As<double>()).IsEqualTo(40.0);
    }

    // ── test_datetime_resample ────────────────────────────────────────────

    [Test]
    public async Task Pandas_DatetimeResample_BucketsEqualExpectedCount()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_datetime_resample():
                idx = pd.date_range("2024-01-01", periods=24, freq="h")
                s = pd.Series(range(24), index=idx, dtype=float)
                # Resample to 3-hour bins: 24/3 = 8 buckets
                resampled = s.resample("3h").sum()
                return int(len(resampled)), float(resampled.iloc[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_datetime_resample");
        using var count = result[0L];
        using var first = result[1L];

        await Assert.That(count.As<int>()).IsEqualTo(8);
        await Assert.That(first.As<double>()).IsEqualTo(3.0); // 0+1+2=3
    }

    // ── test_rolling ──────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Rolling_FirstNaNThenCorrectMean()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            import math
            def test_rolling():
                s = pd.Series([1.0,2.0,3.0,4.0,5.0])
                r = s.rolling(3).mean()
                return bool(math.isnan(r.iloc[0])), bool(math.isnan(r.iloc[1])), float(r.iloc[2]), float(r.iloc[4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_rolling");
        using var nan0 = result[0L];
        using var nan1 = result[1L];
        using var val2 = result[2L];
        using var val4 = result[3L];

        await Assert.That(nan0.As<bool>()).IsTrue();
        await Assert.That(nan1.As<bool>()).IsTrue();
        await Assert.That(val2.As<double>()).IsEqualTo(2.0);
        await Assert.That(val4.As<double>()).IsEqualTo(4.0);
    }

    // ── test_multiindex ───────────────────────────────────────────────────

    [Test]
    public async Task Pandas_MultiIndex_SelectGroupAHasTwoRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_multiindex():
                idx = pd.MultiIndex.from_tuples([("a",1),("a",2),("b",1)])
                df = pd.DataFrame({"v":[10,20,30]}, index=idx)
                sub = df.loc["a"]
                return int(len(sub)), int(sub["v"].iloc[0]), int(sub["v"].iloc[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multiindex");
        using var count = result[0L];
        using var v0 = result[1L];
        using var v1 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(2);
        await Assert.That(v0.As<int>()).IsEqualTo(10);
        await Assert.That(v1.As<int>()).IsEqualTo(20);
    }

    // ── test_categorical ──────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Categorical_DtypeIsCategoryWithThreeCategories()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_categorical():
                s = pd.Series(["red","blue","red","green"], dtype="category")
                return str(s.dtype), int(len(s.cat.categories))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_categorical");
        using var dtype = result[0L];
        using var catCount = result[1L];

        await Assert.That(dtype.As<string>()).IsEqualTo("category");
        await Assert.That(catCount.As<int>()).IsEqualTo(3);
    }

    // ── test_missing_values ───────────────────────────────────────────────

    [Test]
    public async Task Pandas_MissingValues_FilledWithExpectedValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            import math
            def test_missing_values():
                df = pd.DataFrame({"a":[1.0, None, 3.0], "b":[None, 5.0, None]})
                df["a"] = df["a"].fillna(0.0)
                df["b"] = df["b"].fillna(99.0)
                return float(df["a"][1]), float(df["b"][0]), float(df["b"][2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_missing_values");
        using var a1 = result[0L];
        using var b0 = result[1L];
        using var b2 = result[2L];

        await Assert.That(a1.As<double>()).IsEqualTo(0.0);
        await Assert.That(b0.As<double>()).IsEqualTo(99.0);
        await Assert.That(b2.As<double>()).IsEqualTo(99.0);
    }

    // ── test_apply_lambda ─────────────────────────────────────────────────

    [Test]
    public async Task Pandas_ApplyLambda_SquarePlusOneValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_apply_lambda():
                df = pd.DataFrame({"x":[1.0,2.0,3.0]})
                df["y"] = df["x"].apply(lambda v: v**2 + 1)
                return float(df["y"].iloc[0]), float(df["y"].iloc[1]), float(df["y"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_apply_lambda");
        using var y0 = result[0L];
        using var y1 = result[1L];
        using var y2 = result[2L];

        await Assert.That(y0.As<double>()).IsEqualTo(2.0);
        await Assert.That(y1.As<double>()).IsEqualTo(5.0);
        await Assert.That(y2.As<double>()).IsEqualTo(10.0);
    }

    // ── test_string_ops ───────────────────────────────────────────────────

    [Test]
    public async Task Pandas_StringOps_UpperAndLenCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_string_ops():
                df = pd.DataFrame({"name":["alice","bob","carol"]})
                df["upper"] = df["name"].str.upper()
                df["length"] = df["name"].str.len()
                return str(df["upper"].iloc[0]), int(df["length"].iloc[0]), int(df["length"].iloc[1])
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

    // ── test_sort_multi ───────────────────────────────────────────────────

    [Test]
    public async Task Pandas_SortMulti_FirstRowHasSmallestAAndLargestB()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_sort_multi():
                df = pd.DataFrame({"a":[2,1,2,1], "b":[3,4,1,2]})
                out = df.sort_values(["a","b"], ascending=[True,False])
                return int(out["a"].iloc[0]), int(out["b"].iloc[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_sort_multi");
        using var a0 = result[0L];
        using var b0 = result[1L];

        await Assert.That(a0.As<int>()).IsEqualTo(1);
        await Assert.That(b0.As<int>()).IsEqualTo(4);
    }

    // ── test_describe ─────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Describe_MeanAndCountCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_describe():
                df = pd.DataFrame({"score": [10.0, 20.0, 30.0, 40.0, 50.0]})
                desc = df["score"].describe()
                return float(desc["mean"]), float(desc["count"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_describe");
        using var mean = result[0L];
        using var count = result[1L];

        await Assert.That(mean.As<double>()).IsEqualTo(30.0);
        await Assert.That(count.As<double>()).IsEqualTo(5.0);
    }

    // ── test_query ────────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Query_FilterByExpressionOneRow()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_query():
                df = pd.DataFrame({"a":[1,2,3], "b":[4,5,6]})
                out = df.query("a == 2")
                return int(len(out)), int(out["b"].iloc[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_query");
        using var count = result[0L];
        using var b0 = result[1L];

        await Assert.That(count.As<int>()).IsEqualTo(1);
        await Assert.That(b0.As<int>()).IsEqualTo(5);
    }

    // ── test_explode ──────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Explode_ListColumnExpandsToFiveRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_explode():
                df = pd.DataFrame({"id":[1,2], "vals":[[10,20,30],[40,50]]})
                out = df.explode("vals")
                return int(len(out)), int(out["vals"].iloc[0]), int(out["vals"].iloc[4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_explode");
        using var count = result[0L];
        using var first = result[1L];
        using var last = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(5);
        await Assert.That(first.As<int>()).IsEqualTo(10);
        await Assert.That(last.As<int>()).IsEqualTo(50);
    }

    // ── test_melt ─────────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Melt_WideToLongHasFourRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_melt():
                df = pd.DataFrame({"id":[1,2], "x":[10,20], "y":[30,40]})
                out = df.melt(id_vars="id", value_vars=["x","y"])
                return int(len(out)), str(out["variable"].iloc[0]), str(out["variable"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_melt");
        using var count = result[0L];
        using var var0 = result[1L];
        using var var2 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(4);
        await Assert.That(var0.As<string>()).IsEqualTo("x");
        await Assert.That(var2.As<string>()).IsEqualTo("y");
    }

    // ── test_duplicates ───────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Duplicates_DropDuplicateLeavesThreeRows()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_duplicates():
                df = pd.DataFrame({"a":[1,1,2,3,3]})
                out = df.drop_duplicates()
                return int(len(out)), int(out["a"].iloc[0]), int(out["a"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_duplicates");
        using var count = result[0L];
        using var a0 = result[1L];
        using var a2 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(3);
        await Assert.That(a0.As<int>()).IsEqualTo(1);
        await Assert.That(a2.As<int>()).IsEqualTo(3);
    }

    // ── test_rank ─────────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Rank_HighestScoreIsRankOne()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_rank():
                df = pd.DataFrame({"score":[70.0,80.0,90.0]})
                df["rank"] = df["score"].rank(ascending=False)
                return float(df["rank"].iloc[0]), float(df["rank"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_rank");
        using var r0 = result[0L];
        using var r2 = result[1L];

        await Assert.That(r0.As<double>()).IsEqualTo(3.0); // 70 is last
        await Assert.That(r2.As<double>()).IsEqualTo(1.0); // 90 is first
    }

    // ── test_merge_group_transform ────────────────────────────────────────

    [Test]
    public async Task Pandas_MergeGroupTransform_GroupMeanColumnCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_merge_group_transform():
                df = pd.DataFrame({"group":["A","A","B","B"], "value":[10.0,30.0,20.0,40.0]})
                df["group_mean"] = df.groupby("group")["value"].transform("mean")
                # group A mean=20, group B mean=30
                return float(df["group_mean"].iloc[0]), float(df["group_mean"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_merge_group_transform");
        using var aMean = result[0L];
        using var bMean = result[1L];

        await Assert.That(aMean.As<double>()).IsEqualTo(20.0);
        await Assert.That(bMean.As<double>()).IsEqualTo(30.0);
    }

    // ── test_multiindex_xs ────────────────────────────────────────────────

    [Test]
    public async Task Pandas_MultiIndexXs_ThreeRowsReturnedForGroupA()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_multiindex_xs():
                arrays = [["A","A","A","B","B"], [1,2,3,1,2]]
                idx = pd.MultiIndex.from_arrays(arrays, names=("group","id"))
                df = pd.DataFrame({"v":[10,20,30,40,50]}, index=idx)
                xs = df.xs("A", level="group")
                return int(len(xs)), int(xs["v"].iloc[0]), int(xs["v"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multiindex_xs");
        using var count = result[0L];
        using var v0 = result[1L];
        using var v2 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(3);
        await Assert.That(v0.As<int>()).IsEqualTo(10);
        await Assert.That(v2.As<int>()).IsEqualTo(30);
    }

    // ── test_multiindex_swap ──────────────────────────────────────────────

    [Test]
    public async Task Pandas_MultiIndexSwap_SixRowsSortedByInnerLevel()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_multiindex_swap():
                idx = pd.MultiIndex.from_product([["X","Y"], [1,2,3]])
                df = pd.DataFrame({"v":range(6)}, index=idx)
                swapped = df.swaplevel(0,1).sort_index()
                return int(len(swapped)), int(swapped["v"].iloc[0]), int(swapped["v"].iloc[-1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multiindex_swap");
        using var count = result[0L];
        using var first = result[1L];
        using var last = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(6);
        await Assert.That(first.As<int>()).IsEqualTo(0);
        await Assert.That(last.As<int>()).IsEqualTo(5);
    }

    // ── test_pivot_table_multiagg ─────────────────────────────────────────

    [Test]
    public async Task Pandas_PivotTableMultiAgg_SumAndMeanCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_pivot_table_multiagg():
                df = pd.DataFrame({
                    "cat":["a","a","b","b","b"],
                    "sub":["x","y","x","y","y"],
                    "val":[1,2,3,4,5]
                })
                pt = pd.pivot_table(df, values="val", index="cat", columns="sub",
                                    aggfunc=["sum","mean"], fill_value=0)
                return (int(pt.loc["a",("sum","x")]),
                        int(pt.loc["b",("sum","y")]),
                        float(pt.loc["b",("mean","y")]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_pivot_table_multiagg");
        using var sumAx = result[0L];
        using var sumBy = result[1L];
        using var meanBy = result[2L];

        await Assert.That(sumAx.As<int>()).IsEqualTo(1);
        await Assert.That(sumBy.As<int>()).IsEqualTo(9);
        await Assert.That(meanBy.As<double>()).IsEqualTo(4.5);
    }

    // ── test_timeseries_rolling_shift ─────────────────────────────────────

    [Test]
    public async Task Pandas_TimeseriesRollingShift_LengthAndValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_timeseries_rolling_shift():
                idx = pd.date_range("2024-01-01", periods=20, freq="h")
                df = pd.DataFrame({"v": range(20)}, index=idx, dtype=float)
                df2 = df.asfreq("30min").interpolate()
                df2["lag"] = df2["v"].shift(2)
                return int(len(df2)), float(df2["v"].iloc[2]), float(df2["lag"].iloc[4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_timeseries_rolling_shift");
        using var length = result[0L];
        using var v2 = result[1L];
        using var lag4 = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(39);
        await Assert.That(v2.As<double>()).IsEqualTo(1.0);
        await Assert.That(lag4.As<double>()).IsEqualTo(1.0);
    }

    // ── test_categorical_grouping ─────────────────────────────────────────

    [Test]
    public async Task Pandas_CategoricalGrouping_MeanPerOrderedGrade()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_categorical_grouping():
                df = pd.DataFrame({
                    "grade": pd.Categorical(["B","A","C","A","B"],
                                           categories=["A","B","C"], ordered=True),
                    "score": [80,90,70,95,85]
                })
                result = df.groupby("grade", observed=True)["score"].mean()
                return float(result["A"]), float(result["B"]), float(result["C"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_categorical_grouping");
        using var aGrade = result[0L];
        using var bGrade = result[1L];
        using var cGrade = result[2L];

        await Assert.That(aGrade.As<double>()).IsEqualTo(92.5);
        await Assert.That(bGrade.As<double>()).IsEqualTo(82.5);
        await Assert.That(cGrade.As<double>()).IsEqualTo(70.0);
    }

    // ── test_explode_group_apply ──────────────────────────────────────────

    [Test]
    public async Task Pandas_ExplodeGroupApply_RangePerGroupCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_explode_group_apply():
                df = pd.DataFrame({"id":[1,2], "vals":[[1,2,3],[4,5]]})
                df = df.explode("vals")
                df["vals"] = df["vals"].astype(int)
                result = df.groupby("id")["vals"].apply(lambda s: int(s.max() - s.min()))
                return int(result[1]), int(result[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_explode_group_apply");
        using var group1 = result[0L];
        using var group2 = result[1L];

        await Assert.That(group1.As<int>()).IsEqualTo(2); // max(1,2,3)-min(1,2,3)
        await Assert.That(group2.As<int>()).IsEqualTo(1); // max(4,5)-min(4,5)
    }

    // ── test_merge_indicator ──────────────────────────────────────────────

    [Test]
    public async Task Pandas_MergeIndicator_FourRowsWithCorrectMergeFlags()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_merge_indicator():
                left = pd.DataFrame({"id":[1,2,3], "a":[10,20,30]})
                right = pd.DataFrame({"id":[2,3,4], "b":[100,200,300]})
                merged = left.merge(right, on="id", how="outer",
                                    indicator=True, validate="one_to_one")
                merged = merged.sort_values("id").reset_index(drop=True)
                return (int(len(merged)),
                        str(merged["_merge"].iloc[0]),
                        str(merged["_merge"].iloc[1]),
                        str(merged["_merge"].iloc[3]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_merge_indicator");
        using var count = result[0L];
        using var m0 = result[1L];
        using var m1 = result[2L];
        using var m3 = result[3L];

        await Assert.That(count.As<int>()).IsEqualTo(4);
        await Assert.That(m0.As<string>()).IsEqualTo("left_only");
        await Assert.That(m1.As<string>()).IsEqualTo("both");
        await Assert.That(m3.As<string>()).IsEqualTo("right_only");
    }

    // ── test_query_local ──────────────────────────────────────────────────

    [Test]
    public async Task Pandas_QueryLocal_FiltersByLocalVariable()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_query_local():
                df = pd.DataFrame({"x":[1,2,3,4], "y":[10,20,30,40]})
                threshold = 25
                result = df.query("y > @threshold")
                return int(len(result)), int(result["y"].iloc[0]), int(result["y"].iloc[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_query_local");
        using var count = result[0L];
        using var y0 = result[1L];
        using var y1 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(2);
        await Assert.That(y0.As<int>()).IsEqualTo(30);
        await Assert.That(y1.As<int>()).IsEqualTo(40);
    }

    // ── test_nested_apply ─────────────────────────────────────────────────

    [Test]
    public async Task Pandas_NestedApply_AddsDoubleColumnViaGroupby()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd, warnings
            def test_nested_apply():
                df = pd.DataFrame({"g":["A","A","B","B"], "v":[1,2,3,4]})
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore")
                    result = df.groupby("g").apply(lambda g: g.assign(double=g["v"]*2))
                flat = result.reset_index(drop=True)
                return int(len(flat)), int(flat["double"].sum())
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_nested_apply");
        using var count = result[0L];
        using var doubleSum = result[1L];

        await Assert.That(count.As<int>()).IsEqualTo(4);
        await Assert.That(doubleSum.As<int>()).IsEqualTo(20); // (1+2+3+4)*2
    }

    // ── test_melt_pivot_roundtrip ─────────────────────────────────────────

    [Test]
    public async Task Pandas_MeltPivotRoundtrip_OriginalValuesRestored()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_melt_pivot_roundtrip():
                df = pd.DataFrame({"id":[1,2], "a":[10,20], "b":[30,40]})
                melted = df.melt(id_vars="id")
                pivoted = melted.pivot(index="id", columns="variable", values="value")
                pivoted.columns.name = None
                return (int(pivoted.loc[1,"a"]), int(pivoted.loc[1,"b"]),
                        int(pivoted.loc[2,"a"]), int(pivoted.loc[2,"b"]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_melt_pivot_roundtrip");
        using var id1a = result[0L];
        using var id1b = result[1L];
        using var id2a = result[2L];
        using var id2b = result[3L];

        await Assert.That(id1a.As<int>()).IsEqualTo(10);
        await Assert.That(id1b.As<int>()).IsEqualTo(30);
        await Assert.That(id2a.As<int>()).IsEqualTo(20);
        await Assert.That(id2b.As<int>()).IsEqualTo(40);
    }

    // ── test_stack_unstack ────────────────────────────────────────────────

    [Test]
    public async Task Pandas_StackUnstack_ShapeCorrectAfterRoundtrip()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd, numpy as np
            def test_stack_unstack():
                df = pd.DataFrame(
                    np.arange(12).reshape(3,4),
                    columns=pd.MultiIndex.from_product([["A","B"],["x","y"]])
                )
                stacked = df.stack()
                result = stacked.unstack(0)
                return int(stacked.shape[0]), int(stacked.shape[1]), int(result.shape[0]), int(result.shape[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_stack_unstack");
        using var stackedRows = result[0L];
        using var stackedCols = result[1L];
        using var resultRows = result[2L];
        using var resultCols = result[3L];

        await Assert.That(stackedRows.As<int>()).IsEqualTo(6);
        await Assert.That(stackedCols.As<int>()).IsEqualTo(2);
        await Assert.That(resultRows.As<int>()).IsEqualTo(2);
        await Assert.That(resultCols.As<int>()).IsEqualTo(6);
    }

    // ── test_rolling_custom ───────────────────────────────────────────────

    [Test]
    public async Task Pandas_RollingCustom_MaxMinWindowAlwaysThree()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd, math
            def test_rolling_custom():
                df = pd.DataFrame({"v": range(1,11)})
                result = df["v"].rolling(4).apply(lambda x: x.max() - x.min(), raw=True)
                return (bool(math.isnan(result.iloc[0])),
                        bool(math.isnan(result.iloc[2])),
                        float(result.iloc[3]),
                        float(result.iloc[9]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_rolling_custom");
        using var nan0 = result[0L];
        using var nan2 = result[1L];
        using var val3 = result[2L];
        using var val9 = result[3L];

        await Assert.That(nan0.As<bool>()).IsTrue();
        await Assert.That(nan2.As<bool>()).IsTrue();
        await Assert.That(val3.As<double>()).IsEqualTo(3.0); // max(1,2,3,4)-min=3
        await Assert.That(val9.As<double>()).IsEqualTo(3.0); // max(7,8,9,10)-min=3
    }

    // ── test_resample_multiagg ────────────────────────────────────────────

    [Test]
    public async Task Pandas_ResampleMultiAgg_FiveBucketsWithCorrectSum()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_resample_multiagg():
                idx = pd.date_range("2024-01-01", periods=50, freq="min")
                df = pd.DataFrame({"v": range(50)}, index=idx)
                result = df.resample("10min").agg(["sum","mean","max"])
                return (int(len(result)),
                        float(result[("v","sum")].iloc[0]),
                        float(result[("v","max")].iloc[0]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_resample_multiagg");
        using var count = result[0L];
        using var sum0 = result[1L];
        using var max0 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(5);
        await Assert.That(sum0.As<double>()).IsEqualTo(45.0); // 0+1+...+9
        await Assert.That(max0.As<double>()).IsEqualTo(9.0);
    }

    // ── test_nullable_int ─────────────────────────────────────────────────

    [Test]
    public async Task Pandas_NullableInt_FillNaReplacesNullWithValue()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_nullable_int():
                df = pd.DataFrame({"a":[1,2,None,4]}, dtype="Int64")
                result = df.fillna(999)
                return str(result["a"].dtype), int(result["a"].iloc[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_nullable_int");
        using var dtype = result[0L];
        using var filled = result[1L];

        await Assert.That(dtype.As<string>()).IsEqualTo("Int64");
        await Assert.That(filled.As<int>()).IsEqualTo(999);
    }

    // ── test_string_complex ───────────────────────────────────────────────

    [Test]
    public async Task Pandas_StringComplex_UpperVowelCountAndReversed()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_string_complex():
                df = pd.DataFrame({"name":["alice","bob","charlie"]})
                result = df.assign(
                    upper=df["name"].str.upper(),
                    vowel_count=df["name"].str.count(r"[aeiou]"),
                    reversed=df["name"].str[::-1]
                )
                return (str(result["upper"].iloc[0]),
                        int(result["vowel_count"].iloc[0]),
                        str(result["reversed"].iloc[2]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_string_complex");
        using var upper = result[0L];
        using var vowels = result[1L];
        using var rev = result[2L];

        await Assert.That(upper.As<string>()).IsEqualTo("ALICE");
        await Assert.That(vowels.As<int>()).IsEqualTo(3); // a, i, e
        await Assert.That(rev.As<string>()).IsEqualTo("eilrahc");
    }

    // ── test_multiindex_join ──────────────────────────────────────────────

    [Test]
    public async Task Pandas_MultiIndexJoin_OuterJoinSixRowsWithCorrectValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_multiindex_join():
                idx1 = pd.MultiIndex.from_product([["A","B"], [1,2]])
                idx2 = pd.MultiIndex.from_product([["A","B"], [2,3]])
                df1 = pd.DataFrame({"v1":[10,20,30,40]}, index=idx1)
                df2 = pd.DataFrame({"v2":[100,200,300,400]}, index=idx2)
                result = df1.join(df2, how="outer")
                return (int(len(result)),
                        float(result.loc[("A",2),"v1"]),
                        float(result.loc[("A",2),"v2"]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multiindex_join");
        using var count = result[0L];
        using var v1 = result[1L];
        using var v2 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(6);
        await Assert.That(v1.As<double>()).IsEqualTo(20.0);
        await Assert.That(v2.As<double>()).IsEqualTo(100.0);
    }

    // ── test_map_mixed ────────────────────────────────────────────────────

    [Test]
    public async Task Pandas_Map_TransformsEachCellToString()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_map_mixed():
                df = pd.DataFrame({"a":[1,"x",3.14], "b":[True,False,None]})
                result = df.map(lambda v: str(v) + "_t")
                return str(result["a"].iloc[0]), str(result["a"].iloc[1]), str(result["b"].iloc[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_map_mixed");
        using var a0 = result[0L];
        using var a1 = result[1L];
        using var b0 = result[2L];

        await Assert.That(a0.As<string>()).IsEqualTo("1_t");
        await Assert.That(a1.As<string>()).IsEqualTo("x_t");
        await Assert.That(b0.As<string>()).IsEqualTo("True_t");
    }

    // ── test_group_transform_multi ────────────────────────────────────────

    [Test]
    public async Task Pandas_GroupTransformMulti_NormalizedSumIsZero()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_group_transform_multi():
                df = pd.DataFrame({
                    "g":["A","A","B","B","B"],
                    "x":[1.0,2.0,3.0,4.0,5.0],
                    "y":[10.0,20.0,30.0,40.0,50.0]
                })
                df["x_norm"] = df.groupby("g")["x"].transform(lambda s: (s - s.mean()) / s.std())
                df["y_norm"] = df.groupby("g")["y"].transform(lambda s: (s - s.mean()) / s.std())
                x_sum_A = float(df[df["g"]=="A"]["x_norm"].sum())
                y_sum_B = float(df[df["g"]=="B"]["y_norm"].sum())
                return x_sum_A, y_sum_B
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_group_transform_multi");
        using var xSumA = result[0L];
        using var ySumB = result[1L];

        // Sum of z-scores within each group is always 0
        await Assert.That(Math.Abs(xSumA.As<double>())).IsLessThan(1e-10);
        await Assert.That(Math.Abs(ySumB.As<double>())).IsLessThan(1e-10);
    }

    // ── test_complex_pipeline ─────────────────────────────────────────────

    [Test]
    public async Task Pandas_ComplexPipeline_GroupMeanAndSumCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_complex_pipeline():
                users = pd.DataFrame({"id":[1,2,3], "group":["A","A","B"]})
                scores = pd.DataFrame({"id":[1,1,2,3,3], "score":[10.0,20.0,30.0,40.0,50.0]})
                merged = users.merge(scores, on="id")
                agg = merged.groupby("group")["score"].agg(["mean","sum"])
                return float(agg.loc["A","mean"]), float(agg.loc["A","sum"]), float(agg.loc["B","mean"])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_complex_pipeline");
        using var aMean = result[0L];
        using var aSum = result[1L];
        using var bMean = result[2L];

        await Assert.That(aMean.As<double>()).IsEqualTo(20.0); // (10+20+30)/3
        await Assert.That(aSum.As<double>()).IsEqualTo(60.0);
        await Assert.That(bMean.As<double>()).IsEqualTo(45.0); // (40+50)/2
    }

    // ── test_json_normalize ───────────────────────────────────────────────

    [Test]
    public async Task Pandas_JsonNormalize_NestedDictsFlattened()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import pandas as pd
            def test_json_normalize():
                data = [
                    {"id":1, "info":{"age":30, "city":"NY"}},
                    {"id":2, "info":{"age":25, "city":"LA"}},
                    {"id":3, "info":{"age":40, "city":"TX"}}
                ]
                result = pd.json_normalize(data)
                return int(len(result)), int(result["info.age"].iloc[0]), str(result["info.city"].iloc[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_json_normalize");
        using var count = result[0L];
        using var age0 = result[1L];
        using var city1 = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(3);
        await Assert.That(age0.As<int>()).IsEqualTo(30);
        await Assert.That(city1.As<string>()).IsEqualTo("LA");
    }
}
