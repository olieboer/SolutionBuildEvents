namespace SolutionBuildEvents
{
    public class Parameter
    {
        public string[] PreBuildEvent { get; set; } = new string[0];
        public string[] PostBuildEvent { get; set; } = new string[0];
        public string[] ConfigurationChangedEvent { get; set; } = new string[0];
    }
}