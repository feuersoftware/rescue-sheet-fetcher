// Strings.OverrideCulture is process-global static state that many test classes set (and reset in
// Dispose). With xUnit's default class-level parallelism one class's culture leaked into another's
// assertions - seen as an intermittent RunSummaryPrinterTests failure once the brand sources added
// many more test classes. The whole suite runs in a few seconds, so running it sequentially costs
// nothing and removes the race instead of working around it per test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
