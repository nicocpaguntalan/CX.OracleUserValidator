# CX.OracleUserValidator

This is a .NET 10 console application that validates Oracle CRM user accounts from a CSV file.

## What the program does

The application:

1. Reads one or more CSV files from the `Users` folder.
2. Uses the `Username` and `Password` values from each row.
3. Sends an authenticated request to the Oracle CRM API base URL configured in `appsettings.json` or `appsettings.{Environment}.json`.
4. Checks the response:
   - `200 OK` = user has access
   - `401 Unauthorized` = user does not have access
5. Writes the validation results to a new CSV file in the same `Users` folder.

## Quick Start

1. Put a CSV file in the `Users` folder.
2. Make sure it contains `Username` and `Password` columns.
3. Run the console app.
4. Check the generated `UserValidationResults-{DateTime}.csv` file in the same folder.

## Where to add the source file

Place the input CSV file in this folder:

`<application base directory>\Users`

When running from Visual Studio, this is usually the app output folder plus `Users`.

Example:

`bin\Debug\net10.0\Users`

## Input file format

The CSV file must include at least these columns:

```csv
Username,Password
user1,password1
user2,password2
```

A sample file is included in the `Users` folder named `Sample-Users.csv`.

## Output file

The application creates a results file in the same `Users` folder using this format:

`UserValidationResults-{DateTime}.csv`

Example:

`UserValidationResults-20261013-153000.csv`

## Configuration

The Oracle CRM base URL is read from:

- `appsettings.json`
- `appsettings.Test.json`
- `appsettings.Stage.json`
- `appsettings.Production.json`

The active environment is controlled by `DOTNET_ENVIRONMENT`, or defaults to `Test`.

## Running the application

1. Put the user CSV file into the `Users` folder.
2. Update the correct `appsettings.*.json` file if needed.
3. Run the console app.
4. Review the generated results CSV in the same folder.

## Notes

- The app continues processing all users even if one request fails.
- The app writes a row for each user, including unexpected errors.
