# IT Equipment Inventory

Web aplikacija za evidenciju i upravljanje informatičkom opremom, zaposlenicima, radnim nalozima, tonerima i tintama.

## Tehnologije

- ASP.NET Core MVC
- Entity Framework Core
- SQLite
- Bootstrap / custom CSS
- EPPlus za Excel import/export
- OpenXML + LibreOffice za generiranje PDF dokumenata

## Funkcionalnosti

- evidencija IT opreme
- zaduživanje i razduživanje opreme
- evidencija zaposlenika
- evidencija radnih naloga / lokacija
- import i export Excel datoteka
- generiranje PDF zaduženja
- korisničke uloge Admin/User
- kontrola tonera i tinti
- mjesečna evidencija potrošnje
- koš za nedavno obrisane stavke
- korisnički profil

## Pokretanje lokalno

```bash
dotnet restore
dotnet ef database update
dotnet run
