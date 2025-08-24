// InvestmentCalculator.h
#ifndef INVESTMENT_CALCULATOR_H
#define INVESTMENT_CALCULATOR_H

#include <iostream>
#include <iomanip>
#include <vector>

using namespace std;

// Class to handle investment calculations
class InvestmentCalculator {
public:
    // Constructor
    InvestmentCalculator(double initialInvestment, double monthlyDeposit,
        double annualInterest, int years);

    // Public methods
    void calculateInvestment();
    void displayDataInput() const;
    void displayResults() const;

private:
    // Private member variables with m_ prefix
    double m_initialInvestment;
    double m_monthlyDeposit;
    double m_annualInterestRate;
    int m_years;

    // Data structure to store yearly results
    struct YearlyData {
        int year;
        double yearEndBalance;
        double yearlyInterest;
    };

    // Vectors to store calculated results
    vector<YearlyData> m_dataWithoutMonthlyDeposits;
    vector<YearlyData> m_dataWithMonthlyDeposits;
};

#endif // INVESTMENT_CALCULATOR_H