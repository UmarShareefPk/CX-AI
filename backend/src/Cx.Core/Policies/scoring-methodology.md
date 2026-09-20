# Customer Satisfaction Scoring Methodology

## The two survey questions
Every customer who buys a new car receives a survey invitation. The survey has exactly two questions.
Question 1 asks: "How would you recommend Honda to your friends and family?"
Question 2 asks: "Did you have any missing part or defect in your new car?"
Both questions are mandatory, and a survey response is only counted once both have been answered.

## Question 1 scoring: likelihood to recommend
Each answer to Question 1 (recommend) maps to a fixed score:
- Highly recommend = 100 points
- Recommend = 50 points
- Might recommend = 25 points
- Not recommend = 0 points
A "Highly recommend" answer is the only one that scores the full 100. A customer who is merely satisfied and answers "Recommend" contributes only 50, so the recommend question rewards delighted customers, not just satisfied ones.

## Question 2 scoring: missing parts and defects
Each answer to Question 2 (vehicle condition) maps to a fixed score:
- All good (no missing part and no defect) = 100 points
- Part missing = 50 points
- Defect = 50 points
- Part missing and defect (both problems) = 0 points
Any problem with the new car costs the dealer at least 50 points on this question, and a car with both a missing part and a defect scores zero.

## Net score of one survey
The net score of a single survey response is the simple average of the two question scores:
net score = (Question 1 score + Question 2 score) / 2.
Example: a customer who answers "Highly recommend" (100) and "Defect" (50) produces a net score of 75.
Example: a customer who answers "Recommend" (50) and "All good" (100) produces a net score of 75.
Example: a customer who answers "Not recommend" (0) and "Part missing and defect" (0) produces a net score of 0.
The best possible net score is 100 and the worst is 0.

## Dealer score for a time period
The dealer score for a time period is the total of all net scores divided by the number of surveys in that period:
dealer score = sum of net scores / number of surveys.
It is therefore the average net score of every survey the dealer received in the selected date range, on a scale of 0 to 100.
Only surveys submitted inside the selected start and end dates count. A dealer with no surveys in the period has no score.

## What a score means
The platform groups dealer scores into bands to help interpret them:
- 85 and above: Excellent. Customers are delighted and vehicles are arriving in perfect condition.
- 70 to 84.99: Good. Most customers are happy, but some are only satisfied or had a vehicle issue.
- 55 to 69.99: Needs attention. Multiple customers reported problems or were lukewarm about recommending.
- Below 55: Critical. A dealer improvement plan is required.
A score based on fewer than 10 surveys is volatile: one unhappy customer can move it noticeably, so treat small samples with caution.
