# Trader i18n pattern — quick reference

This project uses an in-app localization service (no resx / no satellite assemblies)
to keep the weekend-project footprint small. English is the primary language,
Russian is selectable from the top-nav toggle.

## Adding a new string

1. Open `Services/LocalizationService.cs`.
2. Find the matching namespace block (e.g. `// ----- Bank -----`,
   `// ----- Bar -----`, `// ----- Stock market -----`, `// ----- Events -----`…).
3. Append a line in the same shape:

   ```csharp
   ["namespace.key"] = ("English text", "Русский текст"),
   ```

4. Use a short dotted key: first segment = screen namespace
   (`bank`, `bar`, `inv`, `prod`, `log`, `stock`, `house`, `save`, `events`, `common`, …),
   second segment = `snake_case` role (`title`, `balance`, `buy_btn`, `no_money`).
5. For parameterized messages use `{0}`, `{1}` — both translations must have the
   same placeholders:

   ```csharp
   ["bank.loan_approved"] = ("✅ Loan {0} for {1} months at {2}",
                             "✅ Кредит {0} на {1} мес. под {2}"),
   ```

## Using a string in a Razor component

```razor
@inject LocalizationService Loc
@implements IDisposable

<h2>@Loc["bank.title"]</h2>
<p>@string.Format(Loc["bank.loan_approved"], amount, term, rate)</p>

@code {
    protected override void OnInitialized()
    {
        Loc.OnLanguageChanged += Refresh;
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Loc.OnLanguageChanged -= Refresh;
    }
}
```

The subscribe / unsubscribe pair is **required** — without it the page won't
re-render when the user flips the language toggle.

## Fallback behavior

`LocalizationService.T(key)` returns the key itself if it isn't in the map, so a
missing translation shows as `bank.missing_key` on screen rather than a blank or
a hard crash. Fix it by adding the key.

## Known gaps

Several service-layer methods still return Russian result strings
(`GameEngine.BuyProperty`, `Bank.TakeLoan`, `SaveGameService.SaveGame`,
`CorporateActionService.*`). The UI detects success/failure by scanning those
strings for Russian stems — e.g. `result.Contains("Поздравляем")`,
`result.StartsWith("Продано")`.

When a service gets translated, keep the `.Contains()` guards dual-language
during the transition, e.g.

```csharp
_messageClass = _message.StartsWith("Куплено") || _message.StartsWith("Bought")
    ? "alert-success" : "alert-danger";
```

and remove the Russian stem once the service is fully migrated.

## Preferred pattern for service-generated enums/flavor text

When a service returns a choice from a fixed set of flavor strings, return an
**enum** (or other stable identifier) alongside the string, and localize in the
UI. Example: `TryMeetSomeone` returns a `BarEncounterType?` so `BarView` can
look up `Loc["bar.enc_drink_together"]` etc. without touching the service.

```csharp
// Service
public (string legacyStr, BarEncounterType? type) Meet(Player p) { ... }

// UI
var localized = type switch {
    BarEncounterType.DrinkTogether => Loc["bar.enc_drink_together"],
    BarEncounterType.BarmanStories => Loc["bar.enc_barman_stories"],
    ...
};
```

This avoids pushing `LocalizationService` into singleton services and keeps all
human-readable strings in one place.

## Language persistence

The chosen language is stored in a browser cookie via JS interop
(`wwwroot/js/language.js`). On the next visit `LocalizationService` reads the
cookie in `OnAfterRenderAsync` and updates before any localized content is
rendered past the first paint.
