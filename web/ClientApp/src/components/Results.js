import React, { Component } from 'react';
import ResultsTable from "./ResultsTable";

export class Results extends Component {
    displayName = Results.name

  constructor(props) {
    super(props);
    this.state = { results: [], loading: true };

    fetch('api/Results/All')
      .then(response => response.json())
      .then(data => {
        this.setState({ results: data, loading: false });
      });
  }

  static render(results) {
    return (
      <div>
      {results.map(result =>
        <div key={result.name}>
          <h4>{result.name}</h4>
          <span>poäng: {result.points}</span>
            <ResultsTable matches={result.matches}/>     
        </div>
      )}
     </div> 
    );
  }

  render() {
    let contents = this.state.loading
      ? <p><em>Laddar...</em></p>
        : Results.render(this.state.results);

    return (
      <div>
        <h1>Alla resultat</h1>
        {contents}
      </div>
    );
  }
}
