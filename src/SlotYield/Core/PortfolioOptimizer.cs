using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

public sealed record PortfolioCandidate(uint ItemId, Quality Quality, decimal CapitalPerSlot, decimal GilPerDay);

public sealed record PortfolioPlan
{
    public PortfolioPlan(
        IReadOnlyList<PortfolioCandidate> picks,
        decimal gilPerDay,
        decimal capital,
        decimal shadowSlotValue,
        decimal marginalGilPerMillion,
        bool truncated = false)
    {
        this.Picks = picks;
        this.GilPerDay = gilPerDay;
        this.Capital = capital;
        this.ShadowSlotValue = shadowSlotValue;
        this.MarginalGilPerMillion = marginalGilPerMillion;
        this.Truncated = truncated;
    }

    public static PortfolioPlan Empty { get; } = new(Array.Empty<PortfolioCandidate>(), 0m, 0m, 0m, 0m);

    public IReadOnlyList<PortfolioCandidate> Picks { get; }

    public decimal GilPerDay { get; }

    public decimal Capital { get; }

    /// <summary>What the weakest slot in the plan earns per day.</summary>
    public decimal ShadowSlotValue { get; }

    /// <summary>Extra gil/day per additional million gil of budget, at the margin.</summary>
    public decimal MarginalGilPerMillion { get; }

    /// <summary>True when the frontier had to be trimmed; the plan is near-optimal rather than exact.</summary>
    public bool Truncated { get; }
}

/// <summary>
/// Picks the set of slots that maximises gil/day subject to (a) how many retainer slots you have
/// and (b) how much gil you are willing to keep tied up in inventory.
///
/// Each candidate occupies one slot. Because capital is a real constraint, this is a 0/1 knapsack
/// in two dimensions (slots x gil). Instead of discretising gil into buckets - which rounds item
/// costs and quietly loses good solutions - this keeps the Pareto frontier of (capital, value)
/// points for each slot count: only points that are not dominated by a cheaper and better one
/// survive. That makes the answer exact for the candidate set.
/// </summary>
public static class PortfolioOptimizer
{
    /// <summary>
    /// Anything beyond this many candidates cannot plausibly make a 20-slot plan and would only
    /// slow the frontier down.
    /// </summary>
    public const int MaxCandidates = 200;

    /// <summary>Safety valve so an adversarial candidate set cannot grow the frontier without bound.</summary>
    public const int MaxFrontierPerSlot = 4000;

    public static PortfolioPlan Optimize(IReadOnlyList<PortfolioCandidate> candidates, int slots, decimal budget)
    {
        if (slots <= 0 || budget <= 0m || candidates.Count == 0)
            return PortfolioPlan.Empty;

        slots = Math.Min(slots, 60);

        var pool = candidates
            .Where(c => c.GilPerDay > 0m && c.CapitalPerSlot >= 0m && c.CapitalPerSlot <= budget)
            .OrderByDescending(c => c.GilPerDay)
            .ThenBy(c => c.CapitalPerSlot)
            .Take(MaxCandidates)
            .ToArray();

        if (pool.Length == 0)
            return PortfolioPlan.Empty;

        // Node 0 is the empty portfolio.
        var nodes = new List<Node> { new(0m, 0m, -1, -1) };
        var frontier = new List<int>[slots + 1];
        for (var s = 0; s <= slots; s++)
            frontier[s] = new List<int>();

        frontier[0].Add(0);
        var truncated = false;

        for (var i = 0; i < pool.Length; i++)
        {
            var item = pool[i];
            var maxSlot = Math.Min(slots, i + 1);

            for (var s = maxSlot; s >= 1; s--)
            {
                if (frontier[s - 1].Count == 0)
                    continue;

                var additions = new List<int>(frontier[s - 1].Count);
                foreach (var previous in frontier[s - 1])
                {
                    var node = nodes[previous];
                    var capital = node.Capital + item.CapitalPerSlot;
                    if (capital > budget)
                        continue;

                    nodes.Add(new Node(capital, node.Value + item.GilPerDay, i, previous));
                    additions.Add(nodes.Count - 1);
                }

                if (additions.Count == 0)
                    continue;

                var merged = Merge(frontier[s], additions, nodes, ref truncated);
                frontier[s] = merged;
            }
        }

        var bestBySlot = new decimal[slots + 1];
        var bestNode = new int[slots + 1];
        Array.Fill(bestNode, -1);

        for (var s = 0; s <= slots; s++)
        {
            foreach (var index in frontier[s])
            {
                if (nodes[index].Value <= bestBySlot[s])
                    continue;

                bestBySlot[s] = nodes[index].Value;
                bestNode[s] = index;
            }
        }

        var bestSlot = 0;
        for (var s = 1; s <= slots; s++)
        {
            if (bestBySlot[s] > bestBySlot[bestSlot])
                bestSlot = s;
        }

        if (bestNode[bestSlot] < 0)
            return PortfolioPlan.Empty;

        // Walk the parent chain back to the empty portfolio.
        var picks = new List<PortfolioCandidate>();
        for (var index = bestNode[bestSlot]; index > 0; index = nodes[index].Previous)
        {
            var node = nodes[index];
            if (node.Item >= 0)
                picks.Add(pool[node.Item]);
        }

        picks.Reverse();

        var totalValue = bestBySlot[bestSlot];
        var totalCapital = picks.Sum(p => p.CapitalPerSlot);

        // Shadow prices: what one more slot, or one more million gil, would add at the margin.
        var previousSlotValue = 0m;
        for (var s = 0; s < bestSlot; s++)
        {
            if (bestBySlot[s] > previousSlotValue)
                previousSlotValue = bestBySlot[s];
        }

        var shadowSlot = totalValue - previousSlotValue;

        // Marginal gil: best value per slot at one million gil less, scaled back up.
        var atReducedBudget = OptimizeWithoutFrontier(pool, slots, budget * 0.95m);
        var marginal = atReducedBudget > 0m && budget > 0m
            ? (totalValue - atReducedBudget) / (budget * 0.05m) * 1_000_000m
            : 0m;

        if (marginal < 0m)
            marginal = 0m;

        return new PortfolioPlan(picks, totalValue, totalCapital, shadowSlot, marginal, truncated);
    }

    /// <summary>Cheap recompute of the optimal value at a different budget (no reconstruction).</summary>
    private static decimal OptimizeWithoutFrontier(IReadOnlyList<PortfolioCandidate> pool, int slots, decimal budget)
    {
        if (budget <= 0m)
            return 0m;

        var frontier = new List<(decimal Capital, decimal Value)>[slots + 1];
        for (var s = 0; s <= slots; s++)
            frontier[s] = new List<(decimal, decimal)>();

        frontier[0].Add((0m, 0m));

        foreach (var item in pool)
        {
            for (var s = slots; s >= 1; s--)
            {
                var additions = new List<(decimal Capital, decimal Value)>();
                foreach (var (capital, value) in frontier[s - 1])
                {
                    var next = capital + item.CapitalPerSlot;
                    if (next <= budget)
                        additions.Add((next, value + item.GilPerDay));
                }

                if (additions.Count == 0)
                    continue;

                additions.AddRange(frontier[s]);
                additions.Sort((a, b) => a.Capital != b.Capital ? a.Capital.CompareTo(b.Capital) : b.Value.CompareTo(a.Value));

                var pruned = new List<(decimal, decimal)>(additions.Count);
                var bestValue = decimal.MinValue;
                foreach (var point in additions)
                {
                    if (point.Value <= bestValue)
                        continue;

                    pruned.Add(point);
                    bestValue = point.Value;
                }

                frontier[s] = pruned;
            }
        }

        var best = 0m;
        for (var s = 0; s <= slots; s++)
        {
            if (frontier[s].Count > 0 && frontier[s][^1].Value > best)
                best = frontier[s][^1].Value;
        }

        return best;
    }

    private static List<int> Merge(List<int> existing, List<int> additions, List<Node> nodes, ref bool truncated)
    {
        var merged = new int[existing.Count + additions.Count];
        existing.CopyTo(merged, 0);
        additions.CopyTo(merged, existing.Count);

        Array.Sort(merged, (a, b) =>
        {
            var byCapital = nodes[a].Capital.CompareTo(nodes[b].Capital);
            return byCapital != 0 ? byCapital : nodes[b].Value.CompareTo(nodes[a].Value);
        });

        var result = new List<int>(merged.Length);
        var bestValue = decimal.MinValue;

        foreach (var index in merged)
        {
            if (nodes[index].Value <= bestValue)
                continue;

            result.Add(index);
            bestValue = nodes[index].Value;
        }

        if (result.Count <= MaxFrontierPerSlot)
            return result;

        // Keep the strongest half by value density; the plan stays good, just not provably exact.
        truncated = true;
        var keep = result
            .OrderByDescending(i => nodes[i].Value)
            .Take(MaxFrontierPerSlot)
            .ToArray();

        Array.Sort(keep, (a, b) => nodes[a].Capital.CompareTo(nodes[b].Capital));

        var trimmed = new List<int>(keep.Length);
        bestValue = decimal.MinValue;
        foreach (var index in keep)
        {
            if (nodes[index].Value <= bestValue)
                continue;

            trimmed.Add(index);
            bestValue = nodes[index].Value;
        }

        return trimmed;
    }

    private sealed record Node(decimal Capital, decimal Value, int Item, int Previous);
}
