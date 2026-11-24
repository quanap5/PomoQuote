# Weather icon credits

The weather icons (21 conditions with day and night variants, plus thermometer, humidity drop, wind, wind direction arrow, location pin, clock and warning sign) are **original artwork** drawn for this project as vector shapes in code (`Weather/WeatherIcons.cs`, GDI+ paths). There is no third-party artwork, no icon font and no image file behind them. They are dedicated to the public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

Style: flat colour, one dark outline (about 3% of the icon size, thicker on tiny icons), rounded caps, one shared palette. Every icon is drawn into the unit square, so it is sharp at any pixel size; the app draws each one once per size into a cache and never scales a bitmap.

Files here:

- `contact-sheet.png`: every icon at 64 px, then at 16, 20, 24 and 32 px on a light and on a dark background. Regenerate it with
  `$env:HELLOWORLDBOT_ICON_SHEET = "Assets/Weather/contact-sheet.png"; dotnet test HelloWorldBot.Tests --filter ContactSheet`
- `preview-*.png/jpg`: screenshots of the real panel and widget (all conditions by day and night, the offline/stale/loading states, the three wallpapers, the widget at 100/80/60% opacity over three backgrounds, and the text line at 100-200% scale). They are documentation only and are not part of the app.

The weather data itself comes from Open-Meteo; see the README.
