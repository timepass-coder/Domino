using Relay.Sim;
using Xunit;

using Fix = FixMath.F64;

public class SimConstantsTests
{
    // Tolerance 0.001 in fixed-point units.
    // Compared on raw longs so the assertion itself never touches floating point.
    static void AssertClose(Fix actual, Fix expected, string what)
    {
        long diff = System.Math.Abs((actual - expected).Raw);
        long tol = Fix.Ratio1000(1).Raw;

        Assert.True(
            diff <= tol,
            $"{what}: expected raw {expected.Raw}, got raw {actual.Raw}"
        );
    }

    [Fact]
    public void ThetaCritMatchesUnitsDoc()
    {
        // atan(0.18) = 0.178093 rad = 10.204 deg
        AssertClose(
            SimConstants.DominoThetaCrit,
            Fix.Ratio(178093, 1000000),
            "theta_crit"
        );
    }

    [Fact]
    public void ComRadiusMatchesUnitsDoc()
    {
        // sqrt(1 + 0.0324) / 2
        AssertClose(
            SimConstants.DominoComRadius,
            Fix.Ratio(508035, 1000000),
            "d_com"
        );
    }

    [Fact]
    public void InertiaMatchesUnitsDoc()
    {
        // (1 + 0.0324) / 3
        AssertClose(
            SimConstants.DominoInertiaPerMass,
            Fix.Ratio(344133, 1000000),
            "I_pivot"
        );
    }

    [Fact]
    public void TipEnergyMatchesUnitsDoc()
    {
        // 20 * (0.508035 - 0.5)
        AssertClose(
            SimConstants.DominoTipEnergyPerMass,
            Fix.Ratio(160709, 1000000),
            "E_tip"
        );
    }

    [Fact]
    public void TipOmegaMatchesUnitsDoc()
    {
        // sqrt(2*0.160709/0.344133)
        AssertClose(
            SimConstants.DominoTipOmega,
            Fix.Ratio(966390, 1000000),
            "omega_min"
        );
    }

    [Fact]
    public void TunnellingBudgetHolds()
    {
        // MaxSpeed * Dt < DominoThickness
        Fix perTick = SimConstants.MaxSpeed * SimConstants.Dt;

        Assert.True(
            perTick.Raw < SimConstants.DominoThickness.Raw,
            "A body could cross a domino's thickness in one tick."
        );
    }

    [Fact]
    public void SqrtIsExactForPerfectSquares()
    {
        Assert.Equal(
            Fix.FromInt(2).Raw,
            Fix.Sqrt(Fix.FromInt(4)).Raw
        );
    }
}