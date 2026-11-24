using PomoQuote.Mascot;

namespace PomoQuote.Tests;

public class MascotStateTests
{
    private static readonly MascotState[] All = Enum.GetValues<MascotState>();

    [Fact]
    public void Every_state_has_at_least_one_way_out()
    {
        Assert.All(All, state => Assert.NotEmpty(MascotStateGraph.NextStates(state)));
    }

    [Fact]
    public void Every_state_can_be_reached_from_every_other_state()
    {
        foreach (var from in All)
        {
            foreach (var to in All.Where(s => s != from))
            {
                Assert.NotEmpty(MascotStateGraph.PathTo(from, to));
            }
        }
    }

    [Fact]
    public void Every_step_of_every_path_is_an_allowed_transition()
    {
        foreach (var from in All)
        {
            foreach (var to in All)
            {
                var previous = from;
                foreach (var step in MascotStateGraph.PathTo(from, to))
                {
                    Assert.True(MascotStateGraph.CanTransition(previous, step), $"{previous} -> {step} is not allowed");
                    previous = step;
                }

                if (from != to) Assert.Equal(to, previous);
            }
        }
    }

    [Fact]
    public void Going_to_the_same_state_needs_no_steps()
    {
        Assert.All(All, state => Assert.Empty(MascotStateGraph.PathTo(state, state)));
    }

    [Theory]
    [InlineData(MascotState.Sleep, MascotState.Run, new[] { MascotState.Sit, MascotState.Idle, MascotState.Run })]
    [InlineData(MascotState.Walk, MascotState.Sleep, new[] { MascotState.Idle, MascotState.Sit, MascotState.Sleep })]
    [InlineData(MascotState.Jump, MascotState.Idle, new[] { MascotState.Land, MascotState.Idle })]
    [InlineData(MascotState.Drag, MascotState.Idle, new[] { MascotState.Land, MascotState.Idle })]
    [InlineData(MascotState.Surprised, MascotState.Idle, new[] { MascotState.Idle })]
    [InlineData(MascotState.Run, MascotState.Idle, new[] { MascotState.Walk, MascotState.Idle })]
    public void Natural_blends_go_through_the_expected_states(MascotState from, MascotState to, MascotState[] expected)
    {
        Assert.Equal(expected, MascotStateGraph.PathTo(from, to));
    }

    [Theory]
    [InlineData(MascotState.Sleep, MascotState.Run)]
    [InlineData(MascotState.Sleep, MascotState.Jump)]
    [InlineData(MascotState.Jump, MascotState.Idle)]
    [InlineData(MascotState.Land, MascotState.Walk)]
    [InlineData(MascotState.Run, MascotState.Sit)]
    [InlineData(MascotState.Drag, MascotState.Idle)]
    public void The_graph_forbids_popping_between_distant_states(MascotState from, MascotState to)
    {
        Assert.False(MascotStateGraph.CanTransition(from, to));
    }

    [Theory]
    [InlineData(MascotState.Idle, MascotState.Walk)]
    [InlineData(MascotState.Walk, MascotState.Idle)]
    [InlineData(MascotState.Idle, MascotState.Sit)]
    [InlineData(MascotState.Sit, MascotState.Sleep)]
    [InlineData(MascotState.Sleep, MascotState.Sit)]
    [InlineData(MascotState.Jump, MascotState.Land)]
    [InlineData(MascotState.Land, MascotState.Idle)]
    [InlineData(MascotState.Surprised, MascotState.Idle)]
    [InlineData(MascotState.Drag, MascotState.Land)]
    [InlineData(MascotState.Walk, MascotState.Shout)]
    public void The_main_transitions_are_allowed(MascotState from, MascotState to)
    {
        Assert.True(MascotStateGraph.CanTransition(from, to));
    }

    [Fact]
    public void Any_state_can_be_picked_up()
    {
        foreach (var state in All.Where(s => s != MascotState.Drag))
        {
            Assert.True(MascotStateGraph.CanTransition(state, MascotState.Drag), $"{state} cannot be dragged");
        }
    }
}
