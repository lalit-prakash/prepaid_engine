using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ConsumerBillingFactsTests
{
    private static Consumer NewConsumer() =>
        new(Guid.NewGuid(), "ACC-1", "Name", "Street", new SmartMeter(Guid.NewGuid(), "M-1", MeterPhase.ThreePhase), 60m);

    [Fact]
    public void An_LT_consumer_cannot_record_LT_side_metering_or_opt_into_maintenance_charges()
    {
        var c = NewConsumer();
        Assert.Throws<ArgumentException>(() => c.SetBillingFacts(null, meteredOnLtSide: true, false, null, false, null));
        Assert.Throws<ArgumentException>(() => c.SetBillingFacts(null, false, transformerMaintenanceOptedIn: true, 100m, false, null));
        Assert.Throws<ArgumentException>(() => c.SetBillingFacts(null, false, false, null, ctPtMaintenanceOptedIn: true, CtPtWiring.ThreePhaseThreeWire));
    }

    [Fact]
    public void Opting_into_TMC_requires_a_positive_transformer_capacity()
    {
        var c = NewConsumer();
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SetBillingFacts(SupplyVoltage.Kv11, false, true, null, false, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SetBillingFacts(SupplyVoltage.Kv11, false, true, 0m, false, null));
    }

    [Fact]
    public void CPMC_requires_LT_side_metering_and_a_wiring()
    {
        var c = NewConsumer();
        Assert.Throws<ArgumentException>(() => c.SetBillingFacts(SupplyVoltage.Kv11, meteredOnLtSide: false, false, null, true, CtPtWiring.ThreePhaseThreeWire));
        Assert.Throws<ArgumentException>(() => c.SetBillingFacts(SupplyVoltage.Kv11, meteredOnLtSide: true, false, null, true, null));
    }

    [Fact]
    public void A_full_valid_set_of_facts_is_recorded()
    {
        var c = NewConsumer();
        c.SetBillingFacts(SupplyVoltage.Kv11, true, true, 100m, true, CtPtWiring.ThreePhaseThreeWire);

        Assert.Equal(SupplyVoltage.Kv11, c.SupplyVoltage);
        Assert.True(c.MeteredOnLtSide);
        Assert.True(c.TransformerMaintenanceOptedIn);
        Assert.Equal(100m, c.TransformerCapacityKva);
        Assert.True(c.CtPtMaintenanceOptedIn);
        Assert.Equal(CtPtWiring.ThreePhaseThreeWire, c.CtPtWiring);
    }

    [Fact]
    public void Opting_out_clears_the_now_irrelevant_capacity_and_wiring()
    {
        var c = NewConsumer();
        c.SetBillingFacts(SupplyVoltage.Kv11, true, true, 100m, true, CtPtWiring.ThreePhaseThreeWire);

        c.SetBillingFacts(SupplyVoltage.Kv11, true, false, null, false, null);

        Assert.False(c.TransformerMaintenanceOptedIn);
        Assert.Null(c.TransformerCapacityKva);
        Assert.False(c.CtPtMaintenanceOptedIn);
        Assert.Null(c.CtPtWiring);
    }
}
