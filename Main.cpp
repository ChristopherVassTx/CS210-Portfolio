// main.cpp
#include "InvestmentCalculator.h"

int main() {
    // Variables for user input
    double initialInvestment = 0.0;
    double monthlyDeposit = 0.0;
    double annualInterest = 0.0;
    int years = 0;

    try {
        // Display welcome message
        cout << "Welcome to the Airgead Banking Investment Calculator" << endl;
        cout << "---------------------------------------------------- " << endl;

        // Get user input
        cout << "Enter initial investment amount: $";
        cin >> initialInvestment;

        cout << "Enter monthly deposit: $";
        cin >> monthlyDeposit;

        cout << "Enter annual interest rate (%): ";
        cin >> annualInterest;

        cout << "Enter number of years: ";
        cin >> years;

        // Basic input validation
        if (initialInvestment < 0 || monthlyDeposit < 0 || annualInterest < 0 || years <= 0) {
            cout << "Error: All values must be positive and years must be greater than zero." << endl;
            return 1;
        }

        // Clear the input buffer to prepare for cin.get()
        cin.ignore();

        // Create calculator object
        InvestmentCalculator calculator(initialInvestment, monthlyDeposit, annualInterest, years);

        // Display input data
        calculator.displayDataInput();

        // Wait for user input before continuing
        cout << "Press enter to continue..." << endl;
        cin.get();  // This will wait for Enter key

        // Calculate investment growth
        calculator.calculateInvestment();

        // Display results
        calculator.displayResults();

        // Wait for user to exit
        cout << endl << "Press enter to exit..." << endl;
        cin.get();

    }
    catch (...) {
        cout << "An unexpected error occurred." << endl;
        return 1;
    }

    return 0;
}