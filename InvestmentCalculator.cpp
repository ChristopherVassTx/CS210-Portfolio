// InvestmentCalculator.cpp
#include "InvestmentCalculator.h"

// Constructor implementation
InvestmentCalculator::InvestmentCalculator(double initialInvestment, double monthlyDeposit,
    double annualInterest, int years) {
    // Initialize member variables
    m_initialInvestment = initialInvestment;
    m_monthlyDeposit = monthlyDeposit;
    m_annualInterestRate = annualInterest;
    m_years = years;
}

// Calculate investment growth for both scenarios
void InvestmentCalculator::calculateInvestment() {
    // Calculate without monthly deposits
    double balance = m_initialInvestment;
    double yearlyInterest = 0.0;

    // Clear previous data
    m_dataWithoutMonthlyDeposits.clear();

    // Calculate for each year
    for (int year = 1; year <= m_years; ++year) {
        // Calculate yearly interest
        yearlyInterest = balance * (m_annualInterestRate / 100);
        // Update balance with interest
        balance += yearlyInterest;

        // Store data for this year
        YearlyData yearData = { year, balance, yearlyInterest };
        m_dataWithoutMonthlyDeposits.push_back(yearData);
    }

    // Calculate with monthly deposits
    balance = m_initialInvestment;
    double previousYearBalance = balance;
    double monthlyInterestRate = m_annualInterestRate / 12 / 100;

    // Clear previous data
    m_dataWithMonthlyDeposits.clear();

    // Calculate for each year
    for (int year = 1; year <= m_years; ++year) {
        previousYearBalance = balance;

        // Calculate for each month
        for (int month = 1; month <= 12; ++month) {
            // Add monthly deposit
            balance += m_monthlyDeposit;
            // Calculate and add monthly interest
            balance += balance * monthlyInterestRate;
        }

        // Calculate yearly interest (final balance minus starting balance minus deposits)
        double yearlyInterest = balance - previousYearBalance - (m_monthlyDeposit * 12);

        // Store data for this year
        YearlyData yearData = { year, balance, yearlyInterest };
        m_dataWithMonthlyDeposits.push_back(yearData);
    }
}

// Display the user's input data
void InvestmentCalculator::displayDataInput() const {
    cout << string(40, '*') << endl;
    cout << string(11, '*') << " Data Input " << string(11, '*') << endl;
    cout << "Initial Investment Amount: $" << fixed << setprecision(2) << m_initialInvestment << endl;
    cout << "Monthly Deposit: $" << fixed << setprecision(2) << m_monthlyDeposit << endl;
    cout << "Annual Interest: " << fixed << setprecision(2) << m_annualInterestRate << "%" << endl;
    cout << "Number of years: " << m_years << endl;
}

// Display calculation results
void InvestmentCalculator::displayResults() const {
    // Display results without monthly deposits
    cout << endl;
    cout << "Balance and Interest Without Additional Monthly Deposits" << endl;
    cout << string(60, '=') << endl;
    cout << setw(10) << "Year"
        << setw(25) << "Year End Balance"
        << setw(25) << "Year End Earned Interest" << endl;
    cout << string(60, '-') << endl;

    for (const auto& yearData : m_dataWithoutMonthlyDeposits) {
        cout << setw(10) << yearData.year
            << setw(25) << "$" << fixed << setprecision(2) << yearData.yearEndBalance
            << setw(25) << "$" << fixed << setprecision(2) << yearData.yearlyInterest
            << endl;
    }

    // Display results with monthly deposits
    cout << endl;
    cout << "Balance and Interest With Additional Monthly Deposits" << endl;
    cout << string(60, '=') << endl;
    cout << setw(10) << "Year"
        << setw(25) << "Year End Balance"
        << setw(25) << "Year End Earned Interest" << endl;
    cout << string(60, '-') << endl;

    for (const auto& yearData : m_dataWithMonthlyDeposits) {
        cout << setw(10) << yearData.year
            << setw(25) << "$" << fixed << setprecision(2) << yearData.yearEndBalance
            << setw(25) << "$" << fixed << setprecision(2) << yearData.yearlyInterest
            << endl;
    }
}