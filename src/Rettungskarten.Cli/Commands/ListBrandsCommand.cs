using System.CommandLine;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class ListBrandsCommand
{
    /// <summary>
    /// One row per <see cref="Brand"/> value, driven by resource keys instead of a hand-maintained
    /// list: <c>Brand_{Brand}_Source</c> (every implemented brand has one; a brand without it is shown
    /// as not implemented) and an optional <c>Brand_{Brand}_Status</c> for brands whose coverage
    /// differs from the per-model, German-language norm (e.g. Porsche's combined PDFs, Lamborghini's English-only sheets), falling
    /// back to Status_FullyFunctional.
    /// </summary>
    internal static (Brand Brand, string Group, string Status, string Source)[] BuildInfo() =>
        Enum.GetValues<Brand>()
            .Select(brand =>
            {
                var source = Strings.TryGet($"Brand_{brand}_Source");
                var status = Strings.TryGet($"Brand_{brand}_Status")
                    ?? Strings.Get(source is null ? "Outcome_NotImplemented" : "Status_FullyFunctional");
                return (brand, DisplayText.For(BrandGroups.GroupOf(brand)), status, source ?? "-");
            })
            .ToArray();

    public static Command Build()
    {
        var command = new Command("brands", Strings.Get("Command_ListBrands_Description"));
        command.SetAction(_ =>
        {
            var brandCol = Strings.Get("Summary_Column_Brand");
            var groupCol = Strings.Get("ListBrands_Column_Group");
            var statusCol = Strings.Get("Summary_Column_Status");
            var sourceCol = Strings.Get("ListBrands_Column_Source");

            var info = BuildInfo();
            // Fixed widths broke as soon as one language's status text (or, since Lamborghini, a
            // brand name) got longer than the others (see RunSummaryPrinter's identical fix) -
            // compute from actual content instead.
            var brandWidth = Math.Max(brandCol.Length, info.Max(i => i.Brand.ToString().Length));
            var groupWidth = Math.Max(groupCol.Length, info.Max(i => i.Group.Length));
            var statusWidth = Math.Max(statusCol.Length, info.Max(i => i.Status.Length));

            Console.WriteLine($"{brandCol.PadRight(brandWidth)} {groupCol.PadRight(groupWidth)} {statusCol.PadRight(statusWidth)} {sourceCol}");
            Console.WriteLine(new string('-', brandWidth + 1 + groupWidth + 1 + statusWidth + 1 + 40));
            foreach (var (brand, group, status, source) in info)
            {
                Console.WriteLine($"{brand.ToString().PadRight(brandWidth)} {group.PadRight(groupWidth)} {status.PadRight(statusWidth)} {source}");
            }

            return 0;
        });

        return command;
    }
}
