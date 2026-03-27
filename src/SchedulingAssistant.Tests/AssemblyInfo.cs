using Xunit;

// Tests share AppSettings._instance (a static singleton) across parallel test classes.
// Disabling parallelization prevents race conditions between classes that call
// AppSettings.Current.Save() concurrently (e.g., WizardDataFlowTests + WizardRoutingTests).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
