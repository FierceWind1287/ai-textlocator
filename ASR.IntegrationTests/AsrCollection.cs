using Xunit;

namespace ASR.IntegrationTests
{
    // The test classes in this collection will run sequentially and share a single AsrFixture instance
    [CollectionDefinition("ASR-collection")]
    public class AsrCollection : ICollectionFixture<AsrFixture> { }
}
