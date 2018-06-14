import React, { Component } from 'react';
import { Col, Grid, Row } from 'react-bootstrap';
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
      <Grid fluid>
        <Row>
        <Col sm={10}>
          <h2>Alla Matcher</h2>
            {contents}
          </Col>
          <Col sm={2}>
          <div>Menu here</div>
          </Col>
        </Row>
      </Grid>
      
      </div>
    );
  }
}
