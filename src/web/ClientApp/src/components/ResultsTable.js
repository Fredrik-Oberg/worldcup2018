import React, { Component } from 'react';

class ResultsTable extends React.Component {
  handleClick = () => {
    this.props.clickHandler(this.props.name);
  };

  render() {

    return (
      <table className='table'>
      <thead>
        <tr>
          <th>Startar</th>
          <th>Match</th>
          <th>Bet</th>
        </tr>
      </thead>
          <tbody>
          {this.props.matches.map((match, i) =>
        <tr key={i}>
          <td>
            <span>{match.matchStart}</span>
          </td>
          <td>
          <span>{match.homeTeam} - {match.awayTeam}</span>
          </td>
          <td>
          <span>{match.result}</span>
          </td>
          </tr>
          )}
      </tbody>
    </table>

    );
  }
}
export default ResultsTable