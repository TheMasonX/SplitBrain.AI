// NUnit reuses the same test class instance across all tests in a fixture by default.
// This attribute restores per-test isolation matching xUnit's behaviour —
// a fresh instance (and therefore fresh NSubstitute mocks / in-memory state) per test case.
[assembly: NUnit.Framework.FixtureLifeCycle(NUnit.Framework.LifeCycle.InstancePerTestCase)]
