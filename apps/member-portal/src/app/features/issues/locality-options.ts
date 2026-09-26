import { Observable, forkJoin, map } from 'rxjs';
import { MasterDataApi } from '../members/master-data.api';

/** One entry in an issue form's Locality select. */
export interface LocalityOption {
  readonly id: string;
  readonly label: string;
}

/**
 * Every Locality in the Samaaj, labelled "Name, City" and sorted.
 *
 * An issue names its locality by id because that is what an Area Admin's
 * review is scoped by: a typed "Hiran Magri" matches nobody's token, so it
 * would leave the issue with the Samaaj Admin alone however it was spelled.
 * The label is also sent, as the issue's display text, so the queue and the
 * detail screen go on reading one field rather than each resolving the id.
 */
export function loadLocalityOptions(api: MasterDataApi): Observable<LocalityOption[]> {
  return forkJoin([api.listLocalities(), api.listCities()]).pipe(
    map(([localities, cities]) => {
      const cityNames = new Map(cities.map((city) => [city.id, city.name]));

      return localities
        .map((locality) => {
          const city = cityNames.get(locality.cityId);
          return { id: locality.id, label: city ? `${locality.name}, ${city}` : locality.name };
        })
        .sort((left, right) => left.label.localeCompare(right.label));
    }),
  );
}
