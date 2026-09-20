# Dealer Ranking and Reporting Periods

## How dealers are ranked
Dealers are ranked by their dealer score for the selected time period, highest score first. Rank 1 is the dealer with the highest score.
The rank is always calculated across all dealers in the network for exactly the same date range, so rank changes when the date range changes.
A dealer's rank can change even if its own score does not, because other dealers' scores change too.

## Ties in ranking
When two or more dealers have the same score (rounded to two decimals) they share the same rank, and the next rank is skipped.
Example: if two dealers tie for second place they are both rank 2, and the next dealer is rank 4.
Dealers with no surveys in the period are not ranked.

## Reporting periods
The platform reports on a selectable date range with a start date and an end date, and both dates are inclusive. Surveys are counted by the date the customer submitted the response.
Standard periods are:
- This month: from the first day of the current calendar month up to today.
- Last month: the previous full calendar month.
- This quarter: from the first day of the current calendar quarter up to today. Quarters are January to March, April to June, July to September, and October to December.
- Last quarter: the previous full calendar quarter.
- Last 30 days and last 90 days: rolling windows ending today.
The dashboard opens on the current month by default, and every page uses the same date filter.

## Comparison with the previous period
The dashboard compares the score with the immediately preceding period of the same length. For a 20 day range the comparison period is the 20 days before the start date. A positive change means the score improved.

## Benchmark against the top dealer
Every dealer can see the score of the top ranked dealer in the network for the period as a benchmark. The identity of other dealers is never shown to dealers; only administrators can see the full leaderboard with dealer names.
The gap to the top dealer (top score minus your score) shows how many points you need to gain to reach rank 1.
