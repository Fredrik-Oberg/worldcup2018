import React, { Component } from 'react';
import ReactTable from "react-table";
import 'react-table/react-table.css'
import Moment from 'react-moment';
import 'moment-timezone';
import 'moment/locale/sv';


const columns = [{
  Header: 'Namn',
  accessor: 'name' 
},
{
  Header: 'Poäng',
  accessor: 'points',
  Cell: props => <span className='number'>{props.value}</span>     
}, {
  Header: 'Resultat',
  accessor: 'result',
}]

export class Home extends Component {
  displayName = Home.name

  constructor(props) {
    super(props);
    this.state = { results: [], loading: true };

    fetch('api/Results/Latest')
      .then(response => response.json())
      .then(data => {
        this.setState({ results: data, loading: false });
      });
  }

  static render(results) {
 
    return (
      <div>
      {results.map(result =>
        <div key={result.matchStart}>
        <div className="text-center">
          <h2>{result.homeTeam} - {result.awayTeam}</h2>
          <h4>
            <Moment date={result.matchStart} 
                    locale="sv"
                    format="LLLL"/>
            </h4>
            </div>
          <ReactTable
            data={result.participants}
            columns={columns}
            filterable={true}
            showPagination={false}
            defaultPageSize={35}
          />
        </div>
      )}
      
     </div> 
    );
  }

  render() {
    let contents = this.state.loading
      ? <p><em>Laddar...</em></p>
        : Home.render(this.state.results);

    return (
      <div>
        {contents}
      </div>
    );
  }
}