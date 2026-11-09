using Xunit;

// Some tests add and delete a scratch plan, which would disturb the tests that go through every plan.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
