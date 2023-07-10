namespace Blazr.Server.Web.Data;

public class WeatherForecastService
{
    private List<WeatherForecast> _forecasts;
    private static readonly string[] Summaries = new[]
        { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"};

    public WeatherForecastService()
        => _forecasts = this.GetForecasts();

    public async ValueTask<IEnumerable<WeatherForecast>> GetForecastsAsync()
    {
        await Task.Delay(1000);
        return _forecasts.AsEnumerable();
    }

    public async ValueTask<WeatherForecast?> GetForecastAsync(int id)
    {
        await Task.Delay(1000);
        return _forecasts.FirstOrDefault(item => item.Id == id);
    }

    private List<WeatherForecast> GetForecasts()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        return Enumerable.Range(1, 10).Select(index => new WeatherForecast
        {
            Id = index,
            Date = date.AddDays(index),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        }).ToList();
    }
}
